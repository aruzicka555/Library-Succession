using Landis.Utilities;
using Landis.Core;
using System.Collections.Generic;
using System.Text;
using System;

namespace Landis.Library.Succession
{
    /// <summary>
    /// An extended base class for text parsers that need to parse basic
    /// harvest parameters -- cohort selectors and species planting list.
    /// </summary>
    public abstract class SuccessionParameterParser<T>
        : Landis.TextParser<T>
    { 
        private bool keywordsEnabled;
        private ISpeciesDataset speciesDataset;
        private InputVar<string> speciesName;
        private Dictionary<ISpecies, uint> dummyDict;

        /// <summary>
        /// Line number where each species was found.  Used to check for
        /// duplicate names in a list.
        /// </summary>
        protected Dictionary<string, int> SpeciesLineNumbers { get; private set; }

        //---------------------------------------------------------------------

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="speciesDataset">
        /// The dataset of species to look up species' names in.
        /// </param>
        /// <param name="keywordsEnabled">
        /// Are keywords like "Oldest" and "AllExceptYoungest" accepted?
        /// </param>
        public SuccessionParameterParser(ISpeciesDataset speciesDataset,
                                    bool keywordsEnabled)
        {
            this.keywordsEnabled = keywordsEnabled;
            this.speciesDataset = speciesDataset;
            this.speciesName = new InputVar<string>("Species");
            this.SpeciesLineNumbers = new Dictionary<string, int>();
        }

        //---------------------------------------------------------------------
        //---------------------------------------------------------------------

        /// <summary>
        /// Read a species name from the current input line.
        /// </summary>
        /// <exception cref="InputValueException">
        /// Thrown if the species name is not valid.
        /// </exception>
        protected ISpecies ReadAndValidateSpeciesName(StringReader currentLine)
        {
            ReadValue(speciesName, currentLine);
            ISpecies species = speciesDataset[speciesName.Value.Actual];
            if (species == null)
                throw new InputValueException(speciesName.Value.String,
                                              "{0} is not a species name",
                                              speciesName.Value.String);
            return species;
        }
        //---------------------------------------------------------------------

        public Planting.SpeciesList ReadDensitySpeciesToPlant()
        {
            //InputVar<List<ISpecies>> plant = new InputVar<List<ISpecies>>(ParameterNames.Plant, ReadSpeciesList);
            InputVar<Dictionary<ISpecies, float>> plant = new InputVar<Dictionary<ISpecies, float>>("Plant", ReadDensitySpeciesList);
            if (ReadOptionalVar(plant))
            {
                List<ISpecies> keyList = new List<ISpecies>(plant.Value.Actual.Keys);
                if(plant.Value.Actual.ContainsValue(-1))
                    return new Planting.SpeciesList(keyList, speciesDataset);
                else
                    return new Planting.SpeciesList(keyList, speciesDataset, plant.Value.Actual);

            }
            else
                return null;
        }

        //---------------------------------------------------------------------

        /// <summary>
        /// Reads a list of species names from the current input line.
        /// </summary>
        public InputValue<Dictionary<ISpecies, float>> ReadDensitySpeciesList(StringReader currentLine,
                                                          out int index)
        {
            List<string> speciesNames = new List<string>();
            //List<ISpecies> speciesList = new List<ISpecies>();
            float plantDensity = -1;

            Dictionary<ISpecies, float> speciesPlanting = new Dictionary<ISpecies, float>();
            TextReader.SkipWhitespace(currentLine);
            index = currentLine.Index;
            while (currentLine.Peek() != -1)
            {
                ISpecies species = ReadAndValidateSpeciesName(currentLine);
                if (speciesPlanting.ContainsKey(species))
                    throw new InputValueException(speciesName.Value.String,
                                                  "The species {0} appears more than once.", species.Name);

                TextReader.SkipWhitespace(currentLine);

                int nextChar = currentLine.Peek();
                if (nextChar == '(')
                {
                    plantDensity = ReadPlantingDensity(currentLine);
                    TextReader.SkipWhitespace(currentLine);
                }
                speciesPlanting.Add(species, plantDensity);
                speciesNames.Add(species.Name);

                //speciesList.Add(species);

                TextReader.SkipWhitespace(currentLine);
            }
            if (speciesNames.Count == 0)
                throw new InputValueException(); // Missing value

            return new InputValue<Dictionary<ISpecies, float>>(speciesPlanting, string.Join(" ", speciesNames.ToArray()));
            //return new InputValue<List<ISpecies>>(speciesList,string.Join(" ", speciesNames.ToArray()));
        }

        //---------------------------------------------------------------------

        public static InputValue<float> ReadPlantingDensity(StringReader reader)
        {
            TextReader.SkipWhitespace(reader);
            //index = reader.Index;

            //  Read left parenthesis
            int nextChar = reader.Peek();
            if (nextChar == -1)
                throw new InputValueException();  // Missing value
            if (nextChar != '(')
                throw MakeInputValueException(TextReader.ReadWord(reader),
                                              "Value does not start with \"(\"");

            StringBuilder valueAsStr = new StringBuilder();
            valueAsStr.Append((char)(reader.Read()));

            //  Read whitespace between '(' and percentage
            valueAsStr.Append(ReadWhitespace(reader));

            //  Read percentage
            string word = ReadWord(reader, ')');
            if (word == "")
                throw MakeInputValueException(valueAsStr.ToString(),
                                              "No value after \"(\"");
            valueAsStr.Append(word);
            float planting;
            try
            {
                planting = (float)Double.Parse(word); // Percentage.Parse(word);
            }
            catch (System.FormatException exc)
            {
                throw MakeInputValueException(valueAsStr.ToString(),
                                              exc.Message);
            }
            if (planting < 0.0 || planting > 100000)
                throw MakeInputValueException(valueAsStr.ToString(),
                                              string.Format("{0} is not between 0% and 100%", word));

            //  Read whitespace and ')'
            valueAsStr.Append(ReadWhitespace(reader));
            char? ch = TextReader.ReadChar(reader);
            if (!ch.HasValue)
                throw MakeInputValueException(valueAsStr.ToString(),
                                              "Missing \")\"");
            valueAsStr.Append(ch.Value);
            if (ch != ')')
                throw MakeInputValueException(valueAsStr.ToString(),
                                              string.Format("Value ends with \"{0}\" instead of \")\"", ch));

            //Landis.Library.Succession.Model.Core.UI.WriteLine("Read in biomass value: {0}", biomass);

            return new InputValue<float>(planting, "Planting density");
        }
        //---------------------------------------------------------------------
        //---------------------------------------------------------------------

        /// <summary>
        /// Creates a new InputValueException for an invalid percentage input
        /// value.
        /// </summary>
        /// <returns></returns>
        public static InputValueException MakeInputValueException(string value,
                                                                  string message)
        {
            return new InputValueException(value,
                                           string.Format("\"{0}\" is not a valid aboveground biomass input", value),
                                           new MultiLineText(message));
        }
        //---------------------------------------------------------------------
        //---------------------------------------------------------------------

        /// <summary>
        /// Reads whitespace from a string reader.
        /// </summary>
        public static string ReadWhitespace(StringReader reader)
        {
            StringBuilder whitespace = new StringBuilder();
            int i = reader.Peek();
            while (i != -1 && char.IsWhiteSpace((char)i))
            {
                whitespace.Append((char)reader.Read());
                i = reader.Peek();
            }
            return whitespace.ToString();
        }
        //---------------------------------------------------------------------
        /// <summary>
        /// Reads a word from a string reader.
        /// </summary>
        /// <remarks>
        /// The word is terminated by whitespace, the end of input, or a
        /// particular delimiter character.
        /// </remarks>
        public static string ReadWord(StringReader reader,
                                      char delimiter)
        {
            StringBuilder word = new StringBuilder();
            int i = reader.Peek();
            while (i != -1 && !char.IsWhiteSpace((char)i) && i != delimiter)
            {
                word.Append((char)reader.Read());
                i = reader.Peek();
            }
            return word.ToString();
        }
    }
}
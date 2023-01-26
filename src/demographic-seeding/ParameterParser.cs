// Copyright 2014 University of Notre Dame
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Landis.Utilities;
using Landis.Core;
using System.Collections.Generic;
//using Landis.Library.Parameters;
using System;
using Landis.Library.SeedDispersal;

namespace Landis.Library.Succession.DemographicSeeding
{
    /// <summary>
    /// A parser that reads the extension's input and output parameters from
    /// a text file.
    /// </summary>
    public class ParameterParser
        : TextParser<Parameters>
    {
        private ISpeciesDataset speciesDataset;
        private Dictionary<string, int> speciesLineNumbers;

        public static class Names
        {
            public const string EmergenceProbabilities = "EmergenceProbabilities";
            public const string SurvivalProbabilities = "SurvivalProbabilities";
            public const string MaxSeedBiomass = "MaxSeedBiomass";
            public const string SeedMass = "SeedMass";
            public const string PnETParameters = "PnETSpeciesParameters";
        }

        //---------------------------------------------------------------------

        static ParameterParser()
        {
            ParsingUtils.RegisterForInputValues();
        }

        //---------------------------------------------------------------------

        public override string LandisDataValue
        {
            get {
                return "Demographic Seeding";
            }
        }

        //---------------------------------------------------------------------

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        public ParameterParser(ISpeciesDataset speciesDataset)
        {
            this.speciesDataset = speciesDataset;
            this.speciesLineNumbers = new Dictionary<string, int>();
        }

        //---------------------------------------------------------------------

        protected override Parameters Parse()
        {
            ReadLandisDataVar();

            Parameters parameters = new Parameters();

            InputVar<Seed_Dispersal.Dispersal_Model> kernel = new InputVar<Seed_Dispersal.Dispersal_Model>("Kernel");
            ReadVar(kernel);
            parameters.Kernel = kernel.Value;

            InputVar<Seed_Dispersal.Seed_Model> seedProduction = new InputVar<Seed_Dispersal.Seed_Model>("SeedProduction");
            ReadVar(seedProduction);
            parameters.SeedProductionModel = seedProduction.Value;

            InputVar<int> monteCarloDraws = new InputVar<int>("MonteCarloDraws");
            ReadVar(monteCarloDraws);
            parameters.MonteCarloDraws = monteCarloDraws.Value;

            InputVar<double> maxLeafArea = new InputVar<double>("MaxLeafArea");
            if (ReadOptionalVar(maxLeafArea))
                parameters.MaxLeafArea = maxLeafArea.Value;
            else
                parameters.MaxLeafArea = Model.Core.CellArea*10000; //m2

            InputVar<double> seedlingLeafArea = new InputVar<double>("SeedlingLeafArea");
            ReadVar(seedlingLeafArea);
            parameters.SeedlingLeafArea = seedlingLeafArea.Value;

            InputVar<double> minCohortProp = new InputVar<double>("MinCohortProp");
            ReadVar(minCohortProp);
            parameters.MinCohortProp = minCohortProp.Value;


            InputVar<string> dispersalProbabilitiesLog = new InputVar<string>("DispersalProbabilitiesLog");
            if (ReadOptionalVar(dispersalProbabilitiesLog))
                parameters.DispersalProbabilitiesLog = dispersalProbabilitiesLog.Value;

            InputVar<string> seedRainMaps = new InputVar<string>("SeedRainMaps");
            if (ReadOptionalVar(seedRainMaps))
                parameters.SeedRainMaps = seedRainMaps.Value;

            InputVar<string> seedlingEmergenceMaps = new InputVar<string>("SeedlingEmergenceMaps");
            if (ReadOptionalVar(seedlingEmergenceMaps))
                parameters.SeedlingEmergenceMaps = seedlingEmergenceMaps.Value;

            parameters.SpeciesParameters = ReadSpeciesParameters();

            ReadEmergenceProbabilities(parameters.SpeciesParameters);

            ReadSurvivalProbabilities(parameters.SpeciesParameters);
            if (parameters.SeedProductionModel == Seed_Dispersal.Seed_Model.BIOMASS)
            {
                //ReadMaxSeedBiomass(parameters.SpeciesParameters);
                parameters.SpeciesParameters = ReadSeedMass(parameters.SpeciesParameters, Names.PnETParameters);
                parameters.SpeciesParameters = ReadPnETParameters(parameters.SpeciesParameters);

                //Calculate seed calibration factor
                CalculateSeedCalibrationFactors(parameters.SpeciesParameters);
            }

            return parameters;
        }

        //---------------------------------------------------------------------

        protected SpeciesParameters[] ReadSpeciesParameters()
        {
            speciesLineNumbers.Clear();  // for re-use during unit testing

            SpeciesParameters[] allSpeciesParameters = new SpeciesParameters[speciesDataset.Count];

            InputVar<string> speciesName = new InputVar<string>("Species");
            InputVar<double> minSeeds = new InputVar<double>("Minimum Seeds Produced");
            InputVar<double> maxSeeds = new InputVar<double>("Maximum Seeds Produced");
            InputVar<double> dispersalMean1 = new InputVar<double>("Dispersal Mean1");
            InputVar<double> dispersalMean2 = new InputVar<double>("Dispersal Mean2");
            InputVar<double> dispersalWeight1 = new InputVar<double>("Dispersal Weight1");

            string lastColumn = "the " + dispersalWeight1.Name + " column";

            while (! AtEndOfInput && CurrentName != Names.EmergenceProbabilities)
            {
                StringReader currentLine = new StringReader(CurrentLine);

                ReadValue(speciesName, currentLine);
                ISpecies species = ValidateSpeciesName(speciesName);

                SpeciesParameters parameters = new SpeciesParameters();

                ReadValue(minSeeds, currentLine);
                parameters.MinSeedsProduced = minSeeds.Value;

                ReadValue(maxSeeds, currentLine);
                parameters.MaxSeedsProduced = maxSeeds.Value;

                ReadValue(dispersalMean1, currentLine);
                parameters.DispersalMean1 = dispersalMean1.Value;

                ReadValue(dispersalMean2, currentLine);
                parameters.DispersalMean2 = dispersalMean2.Value;

                ReadValue(dispersalWeight1, currentLine);
                parameters.DispersalWeight1 = dispersalWeight1.Value;

                CheckNoDataAfter(lastColumn, currentLine);
                allSpeciesParameters[species.Index] = parameters;
                GetNextLine();
            }

            if (speciesLineNumbers.Count == 0)
                throw NewParseException("Expected a line starting with a species name");

            return allSpeciesParameters;
        }

        //---------------------------------------------------------------------
        protected SpeciesParameters[] CalculateSeedCalibrationFactors(SpeciesParameters[] speciesParameters)
        {
            foreach (ISpecies spc in speciesDataset)
            {
                double frActWd = speciesParameters[spc.Index].FrActWd;
                double slwMax = speciesParameters[spc.Index].SLWmax;
                double peakFoliageBiomass = 7.5e12 * frActWd * frActWd - 1.175e9 * frActWd + 60000;
                double fActiveBiom = Math.Exp(-1 * frActWd * peakFoliageBiomass);
                double maxFoliage = speciesParameters[spc.Index].FracFol * fActiveBiom * peakFoliageBiomass;
                //estimate seeds at peak foliage
                double estimateSeeds = (1.0 / slwMax) * Math.Pow(speciesParameters[spc.Index].SeedMass, -0.58) * Math.Pow(maxFoliage / ((maxFoliage / slwMax) + 1), 0.9234);
                //calculate ratio for estimated and actual seeds at peak foliage
                double actualMaxSeeds = speciesParameters[spc.Index].MaxSeedsProduced;
                double seedCalibration = actualMaxSeeds / estimateSeeds;
                speciesParameters[spc.Index].SeedCalibration = seedCalibration;
            }
            return speciesParameters;
        }

        //---------------------------------------------------------------------

        /// <summary>
        /// Validates a species name read from the current input line.
        /// </summary>
        /// <exception cref="InputValueException">
        /// Thrown if the species name is not valid, or if the species name
        /// was previously used on an earlier line in the input file.
        /// </exception>
        protected ISpecies ValidateSpeciesName(InputVar<string> name)
        {
            ISpecies species = speciesDataset[name.Value.Actual];
            if (species == null)
                throw new InputValueException(name.Value.String,
                                              "{0} is not a species name",
                                              name.Value.String);
            int lineNumber;
            if (speciesLineNumbers.TryGetValue(species.Name, out lineNumber))
                throw new InputValueException(name.Value.String,
                                              "The species {0} was previously used on line {1}",
                                              name.Value.String, lineNumber);
            else
                speciesLineNumbers[species.Name] = LineNumber;

            return species;
        }

        //---------------------------------------------------------------------

        protected void ReadEmergenceProbabilities(SpeciesParameters[] allSpeciesParameters)
        {
            ReadProbabilities(Names.EmergenceProbabilities,
                              "Emergence Probability",
                              Names.SurvivalProbabilities,
                              allSpeciesParameters,
                              delegate(SpeciesParameters speciesParameters)
                              {
                                  return speciesParameters.EmergenceProbabilities;
                              });
        }

        //---------------------------------------------------------------------

        protected void ReadSurvivalProbabilities(SpeciesParameters[] allSpeciesParameters)
        {
            ReadProbabilities(Names.SurvivalProbabilities,
                              "Survival Probability",
                              Names.SeedMass,
                              allSpeciesParameters,
                              delegate(SpeciesParameters speciesParameters)
                              {
                                  return speciesParameters.SurvivalProbabilities;
                              });
        }
        //---------------------------------------------------------------------

        protected void ReadMaxSeedBiomass(SpeciesParameters[] allSpeciesParameters)
        {
            ReadBiomass(Names.MaxSeedBiomass,
                              "Max Seed Biomass",
                              "", // Means end-of-input only since name can never be empty
                              allSpeciesParameters,
                              delegate(SpeciesParameters speciesParameters)
                              {
                                  return speciesParameters.MaxSeedBiomass;
                              });
        }
        //---------------------------------------------------------------------


        // A delegate for accessing a particular array of probabilities for a
        // species.
        protected delegate double[] GetProbabilities(SpeciesParameters speciesParameters);

        //---------------------------------------------------------------------

        protected void ReadProbabilities(string              tableName,
                                         string              probabilityName,
                                         string              nameAfterTable,
                                         SpeciesParameters[] allSpeciesParameters,
                                         GetProbabilities    getProbabilities)
        {
            ReadName(tableName);

            speciesLineNumbers.Clear();

            InputVar<string> speciesName = new InputVar<string>("Species");
            InputVar<double> probability = new InputVar<double>(probabilityName);

            IEcoregion lastEcoregion = Model.Core.Ecoregions[Model.Core.Ecoregions.Count-1];
            string lastColumn = "the " + lastEcoregion.Name + " ecoregion column";

            while (!AtEndOfInput && CurrentName != nameAfterTable)
            {
                StringReader currentLine = new StringReader(CurrentLine);

                ReadValue(speciesName, currentLine);
                ISpecies species = ValidateSpeciesName(speciesName);

                SpeciesParameters parameters = allSpeciesParameters[species.Index];
                double[] probabilities = getProbabilities(parameters);

                foreach (IEcoregion ecoregion in Model.Core.Ecoregions)
                {
                    ReadValue(probability, currentLine);
                    if (probability.Value < 0.0 || probability.Value > 1.0)
                        throw new InputValueException(probability.Value.String,
                                                      "Probability for ecoregion " + ecoregion.Name + " is not between 0.0 and 1.0");
                    probabilities[ecoregion.Index] = probability.Value;
                }

                CheckNoDataAfter(lastColumn, currentLine);
                GetNextLine();
            }
        }
        //---------------------------------------------------------------------
        protected SpeciesParameters[] ReadSeedMass(SpeciesParameters[] allSpeciesParameters, string nameAfterTable)
        {           
            ReadName(Names.SeedMass);
            speciesLineNumbers.Clear();

            InputVar<string> speciesName = new InputVar<string>("Species");
            InputVar<double> seedMass = new InputVar<double>("SeedMass");

            while (!AtEndOfInput && CurrentName != nameAfterTable)
            {
                StringReader currentLine = new StringReader(CurrentLine);

                ReadValue(speciesName, currentLine);
                ISpecies species = ValidateSpeciesName(speciesName);

                ReadValue(seedMass, currentLine);
                allSpeciesParameters[species.Index].SeedMass = seedMass.Value; 
                GetNextLine();
            }
            return allSpeciesParameters;
        }
        //---------------------------------------------------------------------
        protected SpeciesParameters[] ReadPnETParameters(SpeciesParameters[] allSpeciesParameters)
        {
            ReadName(Names.PnETParameters);
            speciesLineNumbers.Clear();

            InputVar<string> speciesName = new InputVar<string>("Species");
            InputVar<double> slwMax = new InputVar<double>("SLWMax");
            InputVar<double> fracFol = new InputVar<double>("FracFol");
            InputVar<double> frActWd = new InputVar<double>("FrActWd");

            while (!AtEndOfInput)
            {
                StringReader currentLine = new StringReader(CurrentLine);

                ReadValue(speciesName, currentLine);
                ISpecies species = ValidateSpeciesName(speciesName);

                ReadValue(slwMax, currentLine);
                allSpeciesParameters[species.Index].SLWmax = slwMax.Value;

                ReadValue(fracFol, currentLine);
                allSpeciesParameters[species.Index].FracFol = fracFol.Value;

                ReadValue(frActWd, currentLine);
                allSpeciesParameters[species.Index].FrActWd = frActWd.Value;
                GetNextLine();
            }
            return allSpeciesParameters;
        }
        //---------------------------------------------------------------------
        protected void ReadBiomass(string tableName,
                                         string biomassName,
                                         string nameAfterTable,
                                         SpeciesParameters[] allSpeciesParameters,
                                         GetProbabilities getProbabilities)
        {
            ReadName(tableName);

            speciesLineNumbers.Clear();

            InputVar<string> speciesName = new InputVar<string>("Species");
            InputVar<double> biomass = new InputVar<double>(biomassName);

            IEcoregion lastEcoregion = Model.Core.Ecoregions[Model.Core.Ecoregions.Count - 1];
            string lastColumn = "the " + lastEcoregion.Name + " ecoregion column";

            while (!AtEndOfInput && CurrentName != nameAfterTable)
            {
                StringReader currentLine = new StringReader(CurrentLine);

                ReadValue(speciesName, currentLine);
                ISpecies species = ValidateSpeciesName(speciesName);

                SpeciesParameters parameters = allSpeciesParameters[species.Index];
                double[] biomassValues = getProbabilities(parameters);

                foreach (IEcoregion ecoregion in Model.Core.Ecoregions)
                {
                    ReadValue(biomass, currentLine);
                    if (biomass.Value < 0.0 )
                        throw new InputValueException(biomass.Value.String,
                                                      "MaxSeedBiomass for ecoregion " + ecoregion.Name + " is less than 0");
                    biomassValues[ecoregion.Index] = biomass.Value;
                }

                CheckNoDataAfter(lastColumn, currentLine);
                GetNextLine();
            }
        }
    }
}
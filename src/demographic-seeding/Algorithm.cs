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

using Landis.Core;
using Landis.GeoTiff;
using Landis.SpatialModeling;
using log4net;
using Seed_Dispersal;
using System.Reflection;
using System.Collections.Generic;
using System;
using System.IO;
using Landis.Utilities;
using System.Linq;

namespace Landis.Library.Succession.DemographicSeeding
{
    public class Algorithm
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly bool isDebugEnabled = log.IsDebugEnabled;

        private Seed_Dispersal.Map seedingData;
        private int timeAtLastCall = -99999;

        private string seedRainMaps;
        private string seedlingEmergenceMaps;
        private string dispersalProbabilitiesFilename;

        //---------------------------------------------------------------------

        /// <summary>
        /// Initializes the demographic seeding algorithm
        /// </summary>
        /// <param name="successionTimestep">
        /// The length of the succession extension's timestep (units: years).
        /// </param>
        public Algorithm(int successionTimestep)
        {
            int numTimeSteps;  // the number of succession time steps to loop over
            int maxCohortAge;  // maximum age allowed for any species, in years

            numTimeSteps = (Model.Core.EndTime - Model.Core.StartTime) / successionTimestep;
            maxCohortAge = 0;
            foreach (ISpecies species in Model.Core.Species)
                if (species.Longevity > maxCohortAge)
                    maxCohortAge = species.Longevity;

            // The library's code comments say max_age_steps represents
            // "maximum age allowed for any species, in years", but it's
            // used in the code as though it represents the maximum age of
            // any species' cohort IN NUMBER OF SUCCESSION TIMESTEPS.  So if
            // the oldest species' Longevity is 2,000 years, and the succession
            // timestep is 10 years, then max_age_steps is 200 timesteps.
            int max_age_steps = maxCohortAge / successionTimestep;

            seedingData = new Seed_Dispersal.Map(
                Model.Core.Landscape.Columns,
                Model.Core.Landscape.Rows,
                Model.Core.Species.Count,
                numTimeSteps,
                successionTimestep,
                Model.Core.Ecoregions.Count,
                max_age_steps);
            seedingData.pixel_size = Model.Core.CellLength;

            // Initialize some species parameters from the core.
            foreach (ISpecies species in Model.Core.Species)
            {
                //seedingData.all_species[species.Index].shade_tolerance = species.ShadeTolerance;
                seedingData.all_species[species.Index].reproductive_age = species.Maturity;
                seedingData.all_species[species.Index].reproductive_age_steps = species.Maturity / successionTimestep;
            }

            // Load user-specified parameters
            string path = "demographic-seeding.txt";  // hard-wired for now, so no changes required to succession extensions
            Model.Core.UI.WriteLine("Reading demographic seeding parameters from {0} ...", path);
            ParameterParser parser = new ParameterParser(Model.Core.Species);
            Parameters parameters = Landis.Data.Load<Parameters>(path, parser);

            seedingData.dispersal_model  = parameters.Kernel;
            seedingData.seed_model       = parameters.SeedProductionModel;
            seedingData.mc_draws         = parameters.MonteCarloDraws;
            seedingData.max_leaf_area    = parameters.MaxLeafArea;
            seedingData.seedling_leaf_area = parameters.SeedlingLeafArea;
            seedingData.min_cohort_prop = parameters.MinCohortProp;
            //seedingData.cohort_threshold = parameters.CohortThreshold;

            seedRainMaps          = parameters.SeedRainMaps;
            seedlingEmergenceMaps = parameters.SeedlingEmergenceMaps;

            dispersalProbabilitiesFilename = parameters.DispersalProbabilitiesLog;
         

            foreach (ISpecies species in Model.Core.Species)
            {
                SpeciesParameters speciesParameters = parameters.SpeciesParameters[species.Index]; 
                seedingData.all_species[species.Index].min_seed  = speciesParameters.MinSeedsProduced;
                seedingData.all_species[species.Index].max_seed  = speciesParameters.MaxSeedsProduced;
                seedingData.all_species[species.Index].seed_mass = speciesParameters.SeedMass;
                seedingData.all_species[species.Index].SLWmax = speciesParameters.SLWmax;
                seedingData.all_species[species.Index].seedCalibration = speciesParameters.SeedCalibration;

                CopyArray(speciesParameters.DispersalParameters,
                          seedingData.all_species[species.Index].dispersal_parameters);
                //CopyArray(speciesParameters.EmergenceProbabilities,
                //          seedingData.emergence_probability[species.Index]);
                CopyArray(speciesParameters.SurvivalProbabilities,
                          seedingData.survival_probability[species.Index]);
                CopyArray(speciesParameters.MaxSeedBiomass,
                          seedingData.max_seed_biomass[species.Index]);              
            
            }

            foreach(Site site in Model.Core.Landscape.AllSites)
            {
                int x = site.Location.Column - 1;
                int y = site.Location.Row - 1;
                int ecoIndex = 0;
                IEcoregion siteEco = Model.Core.Ecoregion[site];
                if (siteEco != null)
                {
                    ecoIndex = siteEco.Index;
                }
                seedingData.ecoregion[x][y] = ecoIndex;
                if (site.IsActive)
                {
                    foreach (ISpecies species in Model.Core.Species)
                    {
                        seedingData.emergence_probability[species.Index][x][y] = Reproduction.EstablishmentProbability(species, (ActiveSite)site);
                        seedingData.mature_biomass[species.Index][x][y] = Reproduction.ActiveBiomass(species, (ActiveSite)site);
                        seedingData.fol_mass[species.Index][x][y] = Reproduction.MatureFolMass(species, (ActiveSite)site);
                        seedingData.density_seeds[species.Index][x][y] = Reproduction.DensitySeeds(species, (ActiveSite)site);
                    }
                }

            }
            // Read provided dispersal kernel files
            List<string> fileNames = new List<string>();
            string fileTemplate = parameters.DispersalProbabilitiesLog;
            foreach (ISpecies species in Model.Core.Species)
            {
                string dispersalProbSpeciesFilename = MapPaths.ReplaceTemplateVars(fileTemplate, 0, species.Name);
                if(File.Exists(dispersalProbSpeciesFilename))
                {
                    string firstLine;
                    using (StreamReader reader = new StreamReader(dispersalProbSpeciesFilename))
                    {
                        firstLine = reader.ReadLine() ?? "";
                    }
                    List<string> firstLineList = new List<string>(firstLine.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries));
                    if (firstLineList[0].Replace("\"", "").Replace("'", "") == "LandisData" && firstLineList[1].Replace("\"", "").Replace("'", "") == "DispersalKernel")
                        fileNames.Add(dispersalProbSpeciesFilename);
                    else
                    {
                        string msg = "File " + dispersalProbSpeciesFilename + " does not begin with LandisData  \"DispersalKernel\".";
                        fileNames.Add("NA");
                    }
                }
                else
                {
                    fileNames.Add("NA");
                }
            }
            Dictionary<int, Dictionary<double, double>> dispersalKernels = InitializeFromFiles(fileNames);


            // If any species not provided, calculate dispersal prob
            foreach (ISpecies species in Model.Core.Species)
            {
                if (dispersalKernels[species.Index].Keys.Count() == 0)
                {
                    SpeciesParameters speciesParameters = parameters.SpeciesParameters[species.Index];
                    if (dispersalProbabilitiesFilename != null)
                    {                        
                        // substitute species name into Filename
                        string dispersalProbSpeciesFilename = MapPaths.ReplaceTemplateVars(dispersalProbabilitiesFilename, 0, species.Name);

                        // Truncate DispersalProbabilitiesLog file and write header
                        using (System.IO.StreamWriter file = new System.IO.StreamWriter(dispersalProbSpeciesFilename, false))
                        {
                            file.WriteLine("LandisData	\"DispersalKernel\"");
                            file.WriteLine("");
                            file.WriteLine(">> Comments:  Generated within LANDIS");
                            if (parameters.Kernel == Dispersal_Model.DOUBLE_EXPONENTIAL)
                            {
                                file.WriteLine("LogMessage" + "\t\"" + parameters.Kernel.ToString() + ": Mean1=" + speciesParameters.DispersalMean1 + ", Mean2=" + speciesParameters.DispersalMean2 + ", Weight1=" + speciesParameters.DispersalWeight1 +", MaxSeeds="+ speciesParameters.MaxSeedsProduced.ToString() + ", Timestep=1, PixelSize=" + Model.Core.CellLength.ToString() +"\"");
                            }
                            else if (parameters.Kernel == Dispersal_Model.TWODT)
                            {
                                file.WriteLine("LogMessage" + "\t\"" + parameters.Kernel.ToString() + ": A=" + speciesParameters.DispersalMean1 + ", B=" + speciesParameters.DispersalMean2  + ", MaxSeeds=" + speciesParameters.MaxSeedsProduced.ToString() + ", Timestep=1, PixelSize=" + Model.Core.CellLength.ToString() + "\"");
                            }
                            file.WriteLine("");
                            file.WriteLine("ProbabilityTable");
                        }
                    }
                }
            }            
            seedingData.Initialize(dispersalKernels);
            //TODO modify write function to create correct format - make writing file optional?
            WriteProbabilities(dispersalKernels);
        }
        //---------------------------------------------------------------------
        // populating the dispersal probability lookup table
        public Dictionary<int, Dictionary<double, double>> InitializeFromFiles(List<string> fileNames)
        {
            Dictionary<int, Dictionary<double, double>> dispersalKernels = new Dictionary<int, Dictionary<double, double>>();
            List<string> message = new List<string>();
            int sppIndex = 0;
            
            //TODO
            // Read LogMessage and write to Landis.log
            foreach (string file in fileNames)
            {
                Dictionary<double, double> sppKernel = new Dictionary<double, double>();
                if (file == "NA")
                {
                    message.Add("No dispersal kernel file for species " + sppIndex.ToString() + ".");
                }
                else
                {
                    // Read files, but limit to max window diagonal
                    double max_dist = Model.Core.CellLength * Math.Sqrt(Model.Core.Landscape.Columns * Model.Core.Landscape.Columns + Model.Core.Landscape.Rows * Model.Core.Landscape.Rows);
                    // Read the file and display it line by line.  
                    string line;
                    bool startTable = false;
                    bool exceedMaxDist = false;
                    System.IO.StreamReader textFile = new System.IO.StreamReader(file);
                    while ((line = textFile.ReadLine()) != null)
                    {
                        if(startTable)
                        {
                            string[] words = line.Split(',');
                            double dist = Double.Parse(words[0]);
                            double prob = Double.Parse(words[1]);
                            double round_dist = Math.Round(dist, 8);
                            sppKernel.Add(round_dist, prob);
                            if (dist > max_dist)
                            {
                                exceedMaxDist = true;
                                break;
                            }
                        }
                        if(line == "ProbabilityTable")
                        {
                            startTable = true;
                        }
                    }

                    textFile.Close(); 
                }
                dispersalKernels.Add(sppIndex, sppKernel);
                // TODO
                // Message if exceedMaxDist is false - kernel may be too small

                sppIndex++;
            }
            return dispersalKernels;
        }
        //---------------------------------------------------------------------

        private void CopyArray<TItem>(TItem[] source,
                                      TItem[] destination)
        {
            for (int i = 0; i < source.Length; i++)
                destination[i] = source[i];
        }

        //---------------------------------------------------------------------

        /// <summary>
        /// Seeding algorithm: determines if a species seeds a site.
        /// <param name="species"></param>
        /// <param name="site">Site that may be seeded.</param>
        /// <returns>true if the species seeds the site.</returns>
        public void DoesSpeciesSeedSite(ISpecies   species,
                                        ActiveSite site, out bool established, out double seedlingProportion, ThreadSafeRandom randomGen = null)
        {
            // Is this the first site for the current timestep?
            if (Model.Core.CurrentTime != timeAtLastCall)
            {
                SimulateOneTimestep();
                WriteOutputMaps();
                //WriteProbabilities();  // Right now probabilities will not vary by timestep, so no need to output multiple times.  File is written during initialization.
            }
            timeAtLastCall = Model.Core.CurrentTime;

            int x = site.Location.Column - 1;
            int y = site.Location.Row - 1;
            int s = species.Index;
            double seedlingCount = seedingData.seedlings[s][x][y];

            double seedlingArea = seedingData.seedling_leaf_area;
            double minSeedlingProp = seedingData.min_cohort_prop;

            seedlingProportion = seedlingCount * seedlingArea / (Model.Core.CellArea*10000); //m2
            if (seedlingProportion < minSeedlingProp)
            {
                established = false;
                seedlingCount = 0;
                seedingData.seedlings[s][x][y] = 0;
                seedingData.cohorts[s][x][y][0] = 0;
            }
            else
                established = true;
            //established = seedlingCount > seedingData.cohort_threshold;
        }

        //---------------------------------------------------------------------
        protected void WriteProbabilities(Dictionary<int, Dictionary<double, double>> dispersalKernels)
        {
            if (dispersalProbabilitiesFilename == null) 
                return;
            foreach (ISpecies species in Model.Core.Species)
            {
                if (dispersalKernels[species.Index].Keys.Count() == 0)
                {
                    string dispersalProbSpeciesFilename = MapPaths.ReplaceTemplateVars(dispersalProbabilitiesFilename, 0, species.Name);
                    using (System.IO.StreamWriter file = new System.IO.StreamWriter(dispersalProbSpeciesFilename, true))
                    {
                        int s = species.Index;
                        List<double> distances = new List<double>(seedingData.GetProbabilityDistances(s));
                        distances.Sort();
                        //file.WriteLine("{0}, {1},,", Model.Core.CurrentTime, species.Name);
                        foreach (double distance in distances)
                        {
                            file.WriteLine("{0},{1}", distance, seedingData.GetDispersalProbability(s, distance));
                        }
                    }
                }
            }
        }
        //---------------------------------------------------------------------
        protected void SimulateOneTimestep()
        {
            if (isDebugEnabled)
                log.DebugFormat("Starting DemographicSeeding.Algorithm.SimulateOneTimestep() ...");

            // Reset mapped values
            foreach (ActiveSite site in Model.Core.Landscape)
            {
                int x = site.Location.Column - 1;
                int y = site.Location.Row - 1;
                foreach (ISpecies species in Model.Core.Species)
                {
                    int s = species.Index;
                    Array.Clear(seedingData.cohorts[s][x][y], 0, seedingData.cohorts[s][x][y].Length);
                    Array.Clear(seedingData.seed_emergence[s][x][y], 0, seedingData.seed_emergence[s][x][y].Length);
                    Array.Clear(seedingData.seed_production[s][x][y], 0, seedingData.seed_production[s][x][y].Length);
                    Array.Clear(seedingData.seed_shadow[s][x][y], 0, seedingData.seed_shadow[s][x][y].Length);
                    seedingData.seedlings[s][x][y] = 0;
                    // Update the EmergenceProbability to be equal to EstablishmentProbability from Succession extension
                    seedingData.emergence_probability[s][x][y] = Reproduction.EstablishmentProbability(species, site);
                    if (seedingData.seed_model == Seed_Model.BIOMASS)
                    {
                        //seedingData.mature_biomass[s][x][y] = Reproduction.ActiveBiomass(species, site);
                        seedingData.fol_mass[s][x][y] = Reproduction.MatureFolMass(species, site);
                    }
                    else if (seedingData.seed_model == Seed_Model.DENSITY)
                    {
                        seedingData.density_seeds[s][x][y] = Reproduction.DensitySeeds(species, site);
                    }
                }
            }

            // This section identifies mature cohorts for seeding
            foreach (ActiveSite site in Model.Core.Landscape)
            {
                int x = site.Location.Column - 1;
                int y = site.Location.Row - 1;

                // seedling count high enough to be considered a cohort by the
                // SimOneTimestep method.
                //int cohortThresholdPlus1 = seedingData.cohort_threshold + 1;

                foreach (ISpecies species in Model.Core.Species)
                {
                    int s = species.Index;
                    int a = seedingData.all_species[s].reproductive_age_steps - 1;
                    if (a < 0)
                        a = 0;
                    int seedlingCount = 0;
                    if (Reproduction.MaturePresent(species, site))
                    {
                        // This will cause SimOneTimestep to consider the
                        // species as reproductive at this site.
                        //seedlingCount = cohortThresholdPlus1;
                        seedlingCount = 1;
                    }

                    seedingData.cohorts[s][x][y][a] = seedlingCount;
                }
            }

            if (isDebugEnabled)
                log.DebugFormat("  Calling seedingData.SimOneTimeStep() ...");
            seedingData.SimOneTimeStep();

            if (isDebugEnabled)
                log.DebugFormat("Exiting SimulateOneTimestep()");
        }

        //---------------------------------------------------------------------

        protected void WriteOutputMaps()
        {
            if (seedRainMaps != null)
            {
                WriteMapsForSuccessionTimestep(
                    seedRainMaps,
                    delegate(int x, int y, int s, int t)
                    {
                        return System.Convert.ToInt32(seedingData.seed_shadow[s][x][y][t]);
                    });
            }
            if (seedlingEmergenceMaps != null)
            {
                WriteMapsForSuccessionTimestep(
                    seedlingEmergenceMaps,
                    delegate(int x, int y, int s, int t)
                    {
                        return System.Convert.ToInt32(seedingData.seed_emergence[s][x][y][t]);
                    });
            }
        }

        //---------------------------------------------------------------------

        /// <summary>
        /// Get the data value for a particular species at a site for an output
        /// map.
        /// </summary>
        /// <param name="x">site's column (0-based)</param>
        /// <param name="y">site's row (0-based)</param>
        /// <param name="s">species' index</param>
        /// <param name="t">year in current succession timestep (0-based)</param>
        public delegate int GetSpeciesValueAt(int x,
                                              int y,
                                              int s,
                                              int t);

        //---------------------------------------------------------------------

        /// <summary>
        /// Write species maps for each year in the succession timestep.
        /// </summary>
        protected void WriteMapsForSuccessionTimestep(string            pathTemplate,
                                                      GetSpeciesValueAt getSpeciesValueAt)
        {
            int successionTimestep = seedingData.time_step;
            int firstYearInTimestep = Model.Core.CurrentTime - successionTimestep + 1;
            for (int t = 0; t < successionTimestep; t++)
                WriteMapsForYear(pathTemplate, t, getSpeciesValueAt);
        }

        //---------------------------------------------------------------------

        protected void WriteMapsForYear(string            pathTemplate,
                                        int               yearInTimestep,
                                        GetSpeciesValueAt getSpeciesValueAt)
        {
            int successionTimestep = seedingData.time_step;
            int firstYearInTimestep = Model.Core.CurrentTime - successionTimestep + 1;
            int year = firstYearInTimestep + yearInTimestep;

            foreach (ISpecies species in Model.Core.Species)
            {
                string path = MapPaths.ReplaceTemplateVars(pathTemplate, year, species.Name);
                int s = species.Index;
                using (IOutputRaster<int> outputRaster = Model.Core.CreateRaster<int>(path, Model.Core.Landscape.Dimensions))
                {
                    int pixel = outputRaster.BufferPixel;
                    foreach (Site site in Model.Core.Landscape.AllSites)
                    {
                        int x = site.Location.Column - 1;
                        int y = site.Location.Row - 1;
                        pixel = getSpeciesValueAt(x, y, s, yearInTimestep);
                        outputRaster.WriteBufferPixel();
                    }
                }
            }
        }
    }
}

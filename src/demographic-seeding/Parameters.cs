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
using Landis.Library.SeedDispersal;

namespace Landis.Library.Succession.DemographicSeeding
{
    /// <summary>
    /// Parameters for demographic seeding that the user can specify.
    /// </summary>
    public class Parameters
    {
        public int monteCarloDraws;
        private double maxLeafArea;
        private double minCohortProp;
        private double seedlingLeafArea;
        private string seedRainMaps;
        private string seedlingEmergenceMaps;

        //---------------------------------------------------------------------

        /// <summary>
        /// Identifies a particular dispersal kernel
        /// </summary>
        public Seed_Dispersal.Dispersal_Model Kernel { get; set; }

        //---------------------------------------------------------------------

        /// <summary>
        /// Identifies a particular seed production model
        /// </summary>
        public Seed_Dispersal.Seed_Model SeedProductionModel { get; set; }

        //---------------------------------------------------------------------

        /// <summary>
        /// The number of Monte Carlo draws to use when estimating dispersal
        /// probabilities.
        /// </summary>
        public int MonteCarloDraws
        {
            get
            {
                return monteCarloDraws;
            }
            set
            {
                if (value <= 0)
                    throw new InputValueException(value.ToString(),
                                                  "Monte Carlo draws must be > 0");
                monteCarloDraws = value;
            }
        }

        //---------------------------------------------------------------------

        /// <summary>
        /// Maximum projected seedling canopy area that can be supported in a
        /// cell (default to cellsize^2)
        /// </summary>
        public double MaxLeafArea
        {
            get
            {
                return maxLeafArea;
            }
            set
            {
                if (value <= 0.0 || value > Model.Core.CellArea*(10000)) //m2
                    throw new InputValueException(value.ToString(),
                                                  string.Format("Max leaf area must be > 0 and <= CellArea ({0})",
                                                                Model.Core.CellArea * (10000)));
                maxLeafArea = value;
            }
        }

        //---------------------------------------------------------------------

        /// <summary>
        /// Minimum proportion of max biomass that counts as a cohort
        /// </summary>
        public double MinCohortProp
        {
            get
            {
                return minCohortProp;
            }
            set
            {
                if (value <= 0)
                    throw new InputValueException(value.ToString(),
                                                  "MinCohortProp must be > 0");
                if (value > 1)
                    throw new InputValueException(value.ToString(),
                                                  "MinCohortProp must be <= 1");
                minCohortProp = value;
            }
        }

        //---------------------------------------------------------------------
        /// <summary>
        /// Area (m2) that each seedling occupies
        /// </summary>
        public double SeedlingLeafArea
        {
            get
            {
                return seedlingLeafArea;
            }
            set
            {
                if (value <= 0)
                    throw new InputValueException(value.ToString(),
                                                  "SeedlingLeafArea must be > 0");
                seedlingLeafArea = value;
            }
        }



        //---------------------------------------------------------------------

        /// <summary>
        /// Template for the paths to output maps of seed rain.
        /// </summary>
        public string SeedRainMaps
        {
            get
            {
                return seedRainMaps;
            }
            set
            {
                if (value != null)
                {
                    MapPaths.CheckTemplateVars(value);
                }
                if (value == seedlingEmergenceMaps)
                    throw new InputValueException(value.ToString(),
                                                  "Same template for seedling emergence maps");
                seedRainMaps = value;
            }
        }

        //---------------------------------------------------------------------

        /// <summary>
        /// Template for the paths to output maps of seedling emergence.
        /// </summary>
        public string SeedlingEmergenceMaps
        {
            get
            {
                return seedlingEmergenceMaps;
            }
            set
            {
                if (value != null)
                {
                    MapPaths.CheckTemplateVars(value);
                }
                if (value == seedRainMaps)
                    throw new InputValueException(value.ToString(),
                                                  "Same template for seed rain maps");
                seedlingEmergenceMaps = value;
            }
        }

        //---------------------------------------------------------------------

        /// <summary>
        /// Species parameters related to demographic seeding.
        /// </summary>
        public SpeciesParameters[] SpeciesParameters { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string DispersalProbabilitiesLog{ get; set; }

    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace chemkin2yml;

public static class ThermDatReader
{
    static CultureInfo EN = CultureInfo.InvariantCulture;

    public static List<Species> Read(string file = "THERM.DAT")
    {
        List<Species> species = new();

        using StreamReader reader = new StreamReader(file);

        decimal? globalLowTemperature = null, globalCommonTemperature = null, globalHighTemperature = null;

        while (!reader.EndOfStream)
        {
            /*
THERMO ALL
   200.000  1000.000  6000.000
            */
            string? line = reader.ReadLine().Trim();
            if (line.StartsWith("THERMO"))
            {
                if (line.StartsWith("THERMO ALL"))
                {
                    line = reader.ReadLine();
                    (globalLowTemperature, globalCommonTemperature, globalHighTemperature) = ReadGlobalTemperatures(line!);
                }
                break;
            }

        }

        while (!reader.EndOfStream)
        {
            string? line = reader.ReadLine();
            if (line is null) break;

            if (!line.EndsWith("1")) continue;

            //Species name (must start in column 1)            18A1       1 to 18
            string name = line[..18].Trim();
            /*
            C2H5OH+ Ethanol+  T08/12C  2.H  6.O  1.E -1.G   298.150  6000.000 1000.        1
             7.92588096E+00 1.35959671E-02-4.72181213E-06 7.44887370E-10-4.38727921E-14    2
             9.07982576E+04-1.61681179E+01-1.62663530E-01 3.57026999E-02-2.34581471E-05    3
             3.16839831E-09 2.25076300E-12 9.30140758E+04 2.55855068E+01 9.43525235E+04    4
            */

            //if (speciesNames.Contains(speciesName))

            string line1 = line;
            string line2 = reader.ReadLine()!;
            string line3 = reader.ReadLine()!;
            string line4 = reader.ReadLine()!;



            try
            {
                //if (name == "CH2O HCOH triplet") Debugger.Break(); //information after the name is offset by one (an additional space has been added after the name)
                //if (name == "CH2O HCOH triplet") continue;

                //if (name == "C4H4N2 PYRAZINE" || name == "C4H4N2 PYRIMIDINE") continue; //both contain 'D' instead of 'E"
                // 0.10431658D+02 0.16150995D-01-0.59292286D-05 0.97133853D-09-0.58749686D-13    2

                //if (name == "MgTiO3(cr)" || name == "NaCL(cr)") continue;
                //MgTiO3(cr) contain '0.00000000E 00' instead of '0.00000000E+00'
                //NaCL(cr) contains '0.0       E+00' and '0.0       E 00' instead of '0.00000000E+00'

                //PtH  Platinum Hy contains N/A instead 
                //-1.43552196E-08 5.11516998E-12-1.06560007E+03 5.01444858E+00   N/A             4

                //Date (not used in the code)                      6A1        19 to 24
                string date = line1[18..24]; // "T08/12" date includes the source as first character

                List<FormulaPart> parts = ReadFormula(line1);

                //phase C found in AL2O3(L)
                // Debug.Assert(phase=="S" || phase=="L" || phase=="G");
                string phase = ReadPhase(line1);

                decimal? lowTemperature = ReadLowTemperature(line1) ?? globalLowTemperature;
                decimal? highTemperature = ReadHighTemperature(line1) ?? globalHighTemperature;
                decimal? commonTemperature = ReadCommonTemperature(line1) ?? globalCommonTemperature;

                if (!lowTemperature.HasValue) throw new InvalidDataException("Lowest temperature cannot be resolved.");
                if (!highTemperature.HasValue) throw new InvalidDataException("Highest temperature cannot be resolved.");
                if (!commonTemperature.HasValue) throw new InvalidDataException("Common temperature cannot be resolved.");

                //line 2 
                var coef1 = ReadCoefficientsA1A5ForUpperInterval(line2);
                //line 3
                var coef2 = ReadCoefficientsA6A7ForUpperAndA1A3ForLowerInterval(line3);
                //line 4
                var coef3 = ReadCoefficientsA6A7ForUpperAndA1A3ForLowerInterval(line4);

                double[] lowInterval = new double[] { coef2[2], coef2[3], coef2[4], coef3[0], coef3[1], coef3[2], coef3[3] };
                double[] highInterval = new double[] { coef1[0], coef1[1], coef1[2], coef1[3], coef1[4], coef2[0], coef2[1] };

                species.Add(new Species(name, date, parts, phase, lowTemperature.Value, commonTemperature.Value, highTemperature.Value, lowInterval, highInterval));

            }
            catch (FormatException e)
            {
                Console.WriteLine($"Parsing error for {name}. {e.Message}");
                continue;
            }
        }

        return species;
    }

    private static string ReadPhase(string line1)
    {
        /*
Phase of species (S, L, or G for solid,          A1         45
       liquid, or gas, respectively)         
        */
        return line1[44..45];
    }

    private static List<FormulaPart> ReadFormula(string line1)
    {
        //Atomic symbols and formula                       4(2A1,I3)  25 to 44
        string sAtoms = line1[24..44];
        //"C  8.H 18.   0.   0." file also allows non integer indices for atoms
        //"AG 1.   0.   0.   0."

        const string regMatch = @"(?<atom>\w{1,2})\s*(?<value>\d{1,3}(\.\d?)?)";

        var matchAtoms = Regex.Matches(sAtoms, regMatch);
        List<FormulaPart> parts = new();
        foreach (Match m in matchAtoms)
        {
            string atom = m.Groups["atom"].Value;
            string value = m.Groups["value"].Value;

            decimal dValue = Decimal.Parse(value);
            parts.Add(new FormulaPart(atom, dValue));
        }

        // Atomic symbols and formula (if needed)           2A1,I3     74 to 78   (blank for default)
        Match extraMatch = Regex.Match(line1[73..78], regMatch);
        if (extraMatch.Success)
        {
            Match m = extraMatch;
            string atom = m.Groups["atom"].Value;
            string value = m.Groups["value"].Value;

            decimal dValue = Decimal.Parse(value);
            parts.Add(new FormulaPart(atom, dValue));
        }

        return parts;
    }

    #region Temperatures

    private static (decimal LowestTemperature, decimal CommonTemperature, decimal HighestTemperature) ReadGlobalTemperatures(string line)
    {
        //THERMO ALL MUST BE BEFORE THE LINE
        /*
THERMO ALL  
   200.000  1000.000  6000.000
//Temperature ranges for 2 sets of coefficients:   3F10.0     1 to 30
        */
        decimal lowTemperature = decimal.Parse(line[0..10],EN);
        decimal commonTemperature = decimal.Parse(line[11..20],EN);
        decimal highTemperature = decimal.Parse(line[21..30],EN);

        return (lowTemperature, commonTemperature, highTemperature);
    }


    private static decimal? ReadTemperature(string line, Range r, string temperatureName)
    {
        string s = line[r];
        if (string.IsNullOrWhiteSpace(s)) return null;

        // Common temperature (if needed)                   E8.0       66 to 73 (blank for default)
        try
        {
            decimal value = decimal.Parse(s,EN);
            return value;
        }
        catch (FormatException exception)
        {
            //CH2O+  CH**-OH+   T 9/11C  1.H  2.O  1.E -1.G    298.150  6000.000 1000. --> '0 1000. '
            //CH2O-  CH**-OH-   T 9/11C  1.H  2.O  1.E  1.G    298.150  6000.000 1000. --> '0 1000. '
            //MgCL2(cr)         T10/13MG 1.CL 2.   0.   0.S   200.000   500.000  C  95.21040 1 -->'  C  95.'
            //MgCL2(cr)         T10/13MG 1.CL 2.   0.   0.S   500.000   987.000  C  95.21040 1 --> '  C  95.'
            //MgCL2(L)          T10/13MG 1.CL 2.   0.   0.L   987.000  2000.000  E  95.21040 1 --> '  E  95.'
            //MgCL2(L)          T10/13MG 1.CL 2.   0.   0.L  2000.000  6000.000  E  95.21040 1 --> '  E  95.'

            throw new FormatException($"Could not parse {temperatureName} Temperature: '{s}'", exception);
        }
    }

    //Low temperature                                  E10.0      46 to 55
    private static decimal? ReadLowTemperature(string line1) => ReadTemperature(line1, 45..55, "Low");

    //High temperature                                 E10.0      56 to 65 
    private static decimal? ReadHighTemperature(string line1) => ReadTemperature(line1, 56..65, "High");

    //Common temperature (if needed)                   E8.0       66 to 73 (blank for default)
    private static decimal? ReadCommonTemperature(string line1) => ReadTemperature(line1, 65..73, "Common");

    #endregion

    #region Coefficients

    private static List<double> ReadCoefficients(string line, int lineNumber, int count)
    {
        var matches = Regex.Matches(line, @"(\+|-)?\d\.\d{8}E(\+|-)\d{2}");
        List<double> values = new List<double>(5);
        foreach (Match m in matches)
            values.Add(double.Parse(m.Value,EN));

        if (values.Count < count) throw new FormatException($"Could not parse coefficients in line {lineNumber}: '{line}'");

        return values;
    }


    //Coefficients a1-a5 in Eqs. (19) - (21),          5(E15.0)   1 to 75 for upper temperature interval
    // 6.93847153E+00 6.19707068E-04-2.63744438E-07 4.86398187E-11-3.04692547E-15    2
    private static List<double> ReadCoefficientsA1A5ForUpperInterval(string line2) => ReadCoefficients(line2, 2, 5);


    //Coefficients a6, a7 for upper temperature        5(E15.0)   1 to 75  interval, and a1, a2, and a3 for lower
    //-3.55755088E+04-8.10637726E+01 1.25245480E+01-1.01018826E-02 2.21992610E-04    3
    private static List<double> ReadCoefficientsA6A7ForUpperAndA1A3ForLowerInterval(string line3) => ReadCoefficients(line3, 3, 5);

    //-6.93592790E-08 3.21630852E-11-3.04774255E+04 2.41511097E+01-2.69420567E+04    4
    private static List<double> ReadCoefficientsA4A7ForLowerInterval(string line4) => ReadCoefficients(line4, 4, 4);

    #endregion

}

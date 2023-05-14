
using chemkin2yml;


HashSet<string> toExtract = new HashSet<string>() {"C8H18,n-octane", "C2H5OH+ Ethanol+", "C8H18,isooctane"};
// string toExtract = "C8H18";
// string alias = "C818";

var species = ThermDatReader.Read();


// string dc = ".";
// Console.WriteLine(Decimal.Parse(dc));
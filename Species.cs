namespace chemkin2yml;

public record FormulaPart(string Atom, decimal Quantity);

public record Species(string Name, string Date, List<FormulaPart> Atoms, string Phase,
    decimal LowTemperature, decimal CommonTemperature, decimal HighTemperature,
    double[] LowInterval, double[] HighInterval);

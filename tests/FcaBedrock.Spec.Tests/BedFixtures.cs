namespace FcaBedrock.Spec.Tests;

// The verbatim mini-mushroom.bed contents (the real v2 parallel-array layout),
// embedded so the reader tests are self-contained. Byte-equality against the
// fixture file is covered end-to-end by Golden.Tests.
internal static class BedFixtures
{
    public const string MushroomBed =
        "[Number of Attributes]\n" +
        "5\n" +
        "\n" +
        "[Attributes]\n" +
        "class\n" +
        "bruises?\n" +
        "gill-size\n" +
        "veil-type\n" +
        "ring-number\n" +
        "\n" +
        "[Attribute Categories]\n" +
        "edible,poisonous\n" +
        "bruises,no\n" +
        "broad,narrow\n" +
        "partial,universal\n" +
        "none,one,two\n" +
        "\n" +
        "[Category Values]\n" +
        "e,p\n" +
        "t,f\n" +
        "b,n\n" +
        "p,u\n" +
        "n,o,t\n" +
        "\n" +
        "[Convert Attribute]\n" +
        "False\n" +
        "True\n" +
        "True\n" +
        "True\n" +
        "True\n" +
        "\n" +
        "[Attribute Type]\n" +
        "c\n" +
        "b\n" +
        "c\n" +
        "c\n" +
        "c\n" +
        "\n" +
        "[Restrict To Values]\n" +
        "\n" +
        "\n" +
        "\n" +
        "\n" +
        "\n" +
        "[End]\n";
}

using CustomGolfStats.Migration.Mapping;

namespace CustomGolfStats.Migration.Tests.Mapping;

public sealed class DeterministicIdsTests
{
    [Fact]
    public void Matches_the_rfc_4122_version_5_reference_vector()
    {
        // uuid.uuid5(uuid.NAMESPACE_DNS, "python.org") in Python's standard library.
        var dns = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        Assert.Equal(Guid.Parse("886313e1-3b8a-5372-9b90-0c9aee199e5d"), DeterministicIds.Create(dns, "python.org"));
    }

    [Theory]
    [InlineData("Round", 1, "fab57e6a-08df-5158-99e3-ca066d706ec3")]
    [InlineData("Course", 42, "2919f316-5ec4-51ac-8ff0-73e414319d7e")]
    [InlineData("RoundDetail", 123456, "1034c742-a969-546f-b48e-f771e12b4eac")]
    public void Golftrack_ids_are_fixed(string table, int id, string expected)
    {
        // Vectors computed independently: uuid.uuid5(UUID("f213513d-47c2-41e9-bf0a-dcaf37214dfe"), "golftrack:<table>:<id>").
        Assert.Equal(Guid.Parse(expected), DeterministicIds.For(table, id));
    }

    [Fact]
    public void Ids_differ_by_table_and_id_and_are_version_5()
    {
        var round = DeterministicIds.For("Round", 7);

        Assert.NotEqual(round, DeterministicIds.For("Course", 7));
        Assert.NotEqual(round, DeterministicIds.For("Round", 8));
        Assert.Equal(round, DeterministicIds.For("Round", 7));
        Assert.Equal(5, round.Version);
    }
}

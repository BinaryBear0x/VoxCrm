using VoxCrm.Application.PetOwners;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Audit;
using VoxCrm.Infrastructure.PetOwners;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class DuplicatePhoneTests(PostgresDatabaseFixture database)
{
    private sealed class FixedTenant(Guid clinicId) : ITenantService
    {
        public Guid GetClinicId() => clinicId;
        public bool IsSystemContext => false;
    }

    private PetOwnerService Service(VoxCrm.Infrastructure.Data.VoxCrmDbContext db, Guid clinicId) =>
        new(db, new FixedTenant(clinicId), new DbAuditLogger(db));

    [Theory]
    [InlineData("0500 111 22 33")]
    [InlineData("+90 500 111 22 33")]
    [InlineData("500 111 22 33")]
    [InlineData("0090 500 111 22 33")]
    [InlineData("(0500) 111-22-33")]
    public async Task The_same_number_in_any_spelling_is_a_duplicate(string secondSpelling)
    {
        Guid clinicId;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            clinicId = (await TestData.CreateClinicWithOwnerAsync(db, "Duplicate Phone " + Guid.NewGuid().ToString("N"))).Clinic.ID;
        }

        await using var first = database.CreateDbContext();
        Assert.True((await Service(first, clinicId).CreateAsync(new PetOwner { FirstName = "Bir", LastName = "Sahip", Phone = "0500 111 22 33" })).Succeeded);

        await using var second = database.CreateDbContext();
        var result = await Service(second, clinicId).CreateAsync(new PetOwner { FirstName = "Iki", LastName = "Sahip", Phone = secondSpelling });

        Assert.False(result.Succeeded);
        Assert.Contains("zaten kay", result.Error);
    }

    [Fact]
    public async Task A_different_number_is_not_a_duplicate_and_a_customer_can_keep_their_own_number()
    {
        Guid clinicId;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            clinicId = (await TestData.CreateClinicWithOwnerAsync(db, "Own Phone " + Guid.NewGuid().ToString("N"))).Clinic.ID;
        }

        PetOwner created;
        await using (var db = database.CreateDbContext())
        {
            var first = await Service(db, clinicId).CreateAsync(new PetOwner { FirstName = "Bir", LastName = "Sahip", Phone = "0500 111 22 33" });
            Assert.True(first.Succeeded);
            created = first.Owner!;
        }

        await using (var db = database.CreateDbContext())
            Assert.True((await Service(db, clinicId).CreateAsync(new PetOwner { FirstName = "Iki", LastName = "Sahip", Phone = "0500 111 22 44" })).Succeeded);

        // Saving the customer again with the same number written differently is not a clash with themselves.
        await using (var db = database.CreateDbContext())
        {
            var update = await Service(db, clinicId).UpdateAsync(new PetOwner
            {
                ID = created.ID, ClinicID = clinicId, FirstName = "Bir", LastName = "Sahip", Phone = "+90 500 111 22 33",
            });
            Assert.True(update.Succeeded, update.Error);
        }
    }
}

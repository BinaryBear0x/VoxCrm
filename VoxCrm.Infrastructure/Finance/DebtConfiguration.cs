using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Infrastructure.Finance;

public sealed class DebtConfiguration : IEntityTypeConfiguration<Debt>
{
    public void Configure(EntityTypeBuilder<Debt> builder)
    {
        builder.ToTable(table =>
            table.HasCheckConstraint("CK_Borçlar_Amount_Positive", "\"Amount\" > 0"));

        builder.Property(debt => debt.CancellationReason).HasMaxLength(500);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Infrastructure.Finance;

public sealed class FinanceAccountConfiguration : IEntityTypeConfiguration<FinanceAccount>
{
    public void Configure(EntityTypeBuilder<FinanceAccount> b)
    {
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => new { x.ClinicID, x.SubmissionId }).IsUnique();
        b.Property(x => x.CustomerName).IsRequired();
    }
}
public sealed class FinanceChargeConfiguration : IEntityTypeConfiguration<FinanceCharge>
{
    public void Configure(EntityTypeBuilder<FinanceCharge> b)
    {
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.ToTable(t => t.HasCheckConstraint("CK_FinanceCharges_Amount", "\"Amount\" > 0"));
    }
}
public sealed class FinanceInstallmentConfiguration : IEntityTypeConfiguration<FinanceInstallment>
{
    public void Configure(EntityTypeBuilder<FinanceInstallment> b)
    {
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.ToTable(t => t.HasCheckConstraint("CK_FinanceInstallments_Amount", "\"Amount\" > 0"));
    }
}
public sealed class FinanceReceiptConfiguration : IEntityTypeConfiguration<FinanceReceipt>
{
    public void Configure(EntityTypeBuilder<FinanceReceipt> b)
    {
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.ToTable(t => t.HasCheckConstraint("CK_FinanceReceipts_Amount", "\"Amount\" > 0"));
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Infrastructure.Finance;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable(table =>
            table.HasCheckConstraint("CK_Payments_Amount_Positive", "\"Amount\" > 0"));

        builder.Property(payment => payment.EntryType)
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property(payment => payment.Reason).HasMaxLength(500);

        builder.HasOne(payment => payment.ReversesPayment)
            .WithOne(payment => payment.Reversal)
            .HasForeignKey<Payment>(payment => payment.ReversesPaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(payment => payment.ReversesPaymentId)
            .IsUnique()
            .HasFilter("\"ReversesPaymentId\" IS NOT NULL");
    }
}

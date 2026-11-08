using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Persistence.Configurations;

public sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("ExchangeRates", t =>
        {
            t.HasCheckConstraint("CK_ExchangeRates_Rate", "[Rate] > 0");
            t.HasCheckConstraint("CK_ExchangeRates_Direction", "[FromCurrency] <> [ToCurrency]");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.FromCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(r => r.ToCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(r => r.Rate).HasPrecision(18, ExchangeRate.MaxRateDecimals).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(r => r.SupersededByUserId).HasMaxLength(450);

        // Duas alterações simultâneas da mesma taxa: a segunda falha em vez de sobrescrever.
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder.HasOne<Currency>().WithMany().HasForeignKey(r => r.FromCurrency).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>().WithMany().HasForeignKey(r => r.ToCurrency).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.SupersededByUserId).OnDelete(DeleteBehavior.Restrict);

        // No máximo UMA taxa vigente por direção (garantido pelo banco, mesmo com alterações simultâneas).
        builder.HasIndex(r => new { r.FromCurrency, r.ToCurrency })
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("UX_ExchangeRates_Active_Direction");

        // Histórico por direção, mais recentes primeiro.
        builder.HasIndex(r => new { r.FromCurrency, r.ToCurrency, r.CreatedAt });
    }
}

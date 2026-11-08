using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Persistence.Configurations;

public sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("Currencies", t => t.HasCheckConstraint(
            "CK_Currencies_DecimalPlaces", $"[DecimalPlaces] BETWEEN 0 AND {Currency.MaxDecimalPlaces}"));

        // Mesmo tipo de Accounts.Currency (nchar(3)) para a FK.
        builder.HasKey(c => c.Code);
        builder.Property(c => c.Code).HasMaxLength(3).IsFixedLength();
        builder.Property(c => c.Name).HasMaxLength(60).IsRequired();
        builder.Property(c => c.DecimalPlaces).IsRequired();
        builder.Property(c => c.IsActive).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        // Moedas iniciais. Inseridas na mesma migration, ANTES da FK de Accounts.Currency
        // (contas existentes usam BRL e USD).
        var seedDate = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        builder.HasData(
            new { Code = "BRL", Name = "Real brasileiro", DecimalPlaces = 2, IsActive = true, CreatedAt = seedDate },
            new { Code = "USD", Name = "Dólar americano", DecimalPlaces = 2, IsActive = true, CreatedAt = seedDate },
            new { Code = "EUR", Name = "Euro", DecimalPlaces = 2, IsActive = true, CreatedAt = seedDate });
    }
}

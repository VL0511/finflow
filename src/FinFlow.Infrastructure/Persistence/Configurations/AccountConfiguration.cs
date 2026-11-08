using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Persistence.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.AccountNumber)
            .HasMaxLength(20)
            .IsRequired();
        builder.HasIndex(a => a.AccountNumber).IsUnique();

        builder.Property(a => a.HolderName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.Balance)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(a => a.Currency)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(a => a.IsActive).IsRequired();
        builder.Property(a => a.CreatedAt).IsRequired();

        // Dono (Customer). Usuários são desativados, nunca excluídos: Restrict.
        builder.Property(a => a.OwnerUserId).HasMaxLength(450);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(a => a.OwnerUserId).OnDelete(DeleteBehavior.Restrict);

        // Moeda cadastrada (seed BRL/USD/EUR na mesma migration, antes desta FK).
        builder.HasOne<Currency>().WithMany().HasForeignKey(a => a.Currency).OnDelete(DeleteBehavior.Restrict);

        // Concorrência otimista: se duas transferências alterarem o mesmo saldo ao
        // mesmo tempo, a segunda falha no commit em vez de sobrescrever a primeira.
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder.HasData(SeedAccounts);
    }

    // Contas fictícias para testar os cenários pelo Swagger.
    private static readonly DateTimeOffset SeedDate = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly object[] SeedAccounts =
    [
        new { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), AccountNumber = "12345", HolderName = "Ana Souza",    Balance = 1000.00m, Currency = "BRL", IsActive = true,  CreatedAt = SeedDate },
        new { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), AccountNumber = "67890", HolderName = "Bruno Lima",   Balance = 250.00m,  Currency = "BRL", IsActive = true,  CreatedAt = SeedDate },
        new { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), AccountNumber = "11223", HolderName = "Carla Mendes", Balance = 5000.00m, Currency = "USD", IsActive = true,  CreatedAt = SeedDate },
        new { Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), AccountNumber = "44556", HolderName = "Diego Rocha",  Balance = 300.00m,  Currency = "BRL", IsActive = false, CreatedAt = SeedDate }
    ];
}

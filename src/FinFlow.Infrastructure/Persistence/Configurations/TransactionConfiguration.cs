using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Persistence.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.SourceAccount)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.DestinationAccount)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(t => t.Currency)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        // Snapshot do câmbio (imutável). Em transações anteriores a este recurso, a migration
        // preenche: destino = origem, valor convertido = valor, taxa = 1.
        builder.Property(t => t.DestinationCurrency)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(t => t.DestinationAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(t => t.ExchangeRate)
            .HasPrecision(18, ExchangeRate.MaxRateDecimals)
            .IsRequired();

        builder.HasOne<ExchangeRate>().WithMany().HasForeignKey(t => t.ExchangeRateId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(t => t.RequestedByUserId).HasMaxLength(450);

        builder.Ignore(t => t.IsCrossCurrency);

        builder.Property(t => t.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.FailureReason).HasMaxLength(500);

        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.UpdatedAt);

        builder.Ignore(t => t.IsFinal);

        // Concorrência otimista: garante que só um processo consiga mudar o status
        // a partir de um mesmo estado (ex.: Worker processando x usuário cancelando,
        // ou a mesma mensagem consumida duas vezes em paralelo).
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder.HasIndex(t => t.CreatedAt);
        builder.HasIndex(t => t.Status);

        // Histórico e fluxo de caixa por conta filtram "SourceAccount = @n OR DestinationAccount = @n"
        // e ordenam por CreatedAt (Id, a chave clusterizada, entra no índice como desempate).
        // Dois índices, um por coluna, para o SQL Server buscar nos dois e unir os resultados.
        // Sem INCLUDE de propósito: as chaves nunca mudam após o INSERT, então as transições de
        // status do Worker não tocam nestes índices (medido no P2: INCLUDE de Status/UpdatedAt
        // multiplicava por 4 o custo de cada UPDATE sem ganho de leitura).
        builder.HasIndex(t => new { t.SourceAccount, t.CreatedAt });
        builder.HasIndex(t => new { t.DestinationAccount, t.CreatedAt });
    }
}

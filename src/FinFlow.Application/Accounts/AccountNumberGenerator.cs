using System.Security.Cryptography;

namespace FinFlow.Application.Accounts;

/// <summary>Gera números de conta. Exclusivo do backend: o cliente nunca escolhe o número.</summary>
public interface IAccountNumberGenerator
{
    string Next();
}

/// <summary>
/// 10 dígitos: 9 aleatórios (gerador criptográfico, sem sequência previsível nem enumeração
/// fácil) + 1 dígito verificador de Luhn (detecta erros de digitação na conta de destino).
/// ~900 milhões de combinações; a unicidade é garantida pelo índice UNIQUE do banco e o
/// serviço tenta outro número em caso de colisão.
/// </summary>
public sealed class RandomAccountNumberGenerator : IAccountNumberGenerator
{
    public const int Length = 10;

    public string Next()
    {
        Span<char> digits = stackalloc char[Length];
        digits[0] = (char)('1' + RandomNumberGenerator.GetInt32(9)); // sem zero à esquerda
        for (var i = 1; i < Length - 1; i++)
            digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));

        digits[Length - 1] = (char)('0' + LuhnCheckDigit(digits[..(Length - 1)]));
        return new string(digits);
    }

    /// <summary>Número com o tamanho certo e dígito verificador válido.</summary>
    public static bool IsValid(string? number) =>
        number is { Length: Length } && number.All(char.IsAsciiDigit) && LuhnCheckDigit(number.AsSpan(0, Length - 1)) == number[^1] - '0';

    private static int LuhnCheckDigit(ReadOnlySpan<char> payload)
    {
        var sum = 0;
        for (var i = 0; i < payload.Length; i++)
        {
            var d = payload[payload.Length - 1 - i] - '0';
            if (i % 2 == 0)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
        }
        return (10 - sum % 10) % 10;
    }
}

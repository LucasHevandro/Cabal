using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Shared.Accounts
{
    /// <summary>Classes jogáveis do MVP (GDD: uma corpo a corpo e uma à distância).</summary>
    public enum CharacterClass
    {
        /// <summary>Lâmina: corpo a corpo.</summary>
        Blade = 0,

        /// <summary>Arcanista: à distância.</summary>
        Arcanist = 1,
    }

    /// <summary>Regras de conta, senha e nome de personagem, iguais no cliente (aviso na hora) e no servidor (decisão).</summary>
    public static class AccountRules
    {
        public const int MaxCharactersPerAccount = 3;
        public const int MinAccountLength = 4;
        public const int MaxAccountLength = 20;
        public const int MinPasswordLength = 8;
        public const int MaxPasswordLength = 64;
        public const int MinCharacterNameLength = 3;
        public const int MaxCharacterNameLength = 14;

        public static string ClassDisplayName(CharacterClass characterClass) => characterClass switch
        {
            CharacterClass.Blade => "Lâmina",
            CharacterClass.Arcanist => "Arcanista",
            _ => "Desconhecida",
        };

        /// <summary>Forma canônica do nome da conta (minúsculas, sem espaços nas pontas).</summary>
        public static string NormalizeAccount(string account) => (account ?? "").Trim().ToLowerInvariant();

        /// <summary>Nulo se válida; senão, o motivo em português.</summary>
        public static string? ValidateAccount(string account)
        {
            var value = NormalizeAccount(account);
            if (value.Length < MinAccountLength || value.Length > MaxAccountLength)
                return $"A conta deve ter de {MinAccountLength} a {MaxAccountLength} caracteres.";
            foreach (var c in value)
            {
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_')
                    return "A conta só pode ter letras sem acento, números e _.";
            }
            return null;
        }

        public static string? ValidatePassword(string password)
        {
            password ??= "";
            if (password.Length < MinPasswordLength) return $"A senha deve ter pelo menos {MinPasswordLength} caracteres.";
            if (password.Length > MaxPasswordLength) return $"A senha deve ter no máximo {MaxPasswordLength} caracteres.";
            var hasLetter = false;
            var hasDigit = false;
            foreach (var c in password)
            {
                if (char.IsLetter(c)) hasLetter = true;
                if (char.IsDigit(c)) hasDigit = true;
            }
            return hasLetter && hasDigit ? null : "A senha precisa ter letras e números.";
        }

        /// <summary>Nome do personagem: letras (acentos permitidos), números depois da primeira letra, sem espaços.</summary>
        public static string? ValidateCharacterName(string name)
        {
            var value = (name ?? "").Trim();
            if (value.Length < MinCharacterNameLength || value.Length > MaxCharacterNameLength)
                return $"O nome deve ter de {MinCharacterNameLength} a {MaxCharacterNameLength} caracteres.";
            if (!char.IsLetter(value[0])) return "O nome deve começar com uma letra.";
            foreach (var c in value)
            {
                if (!char.IsLetterOrDigit(c)) return "O nome só pode ter letras e números, sem espaços.";
            }
            return null;
        }

        /// <summary>
        /// Chave para conferir nomes repetidos: sem acentos e em minúsculas ("Ação" e "acao" são o mesmo nome).
        /// </summary>
        public static string NameKey(string name)
        {
            var decomposed = (name ?? "").Trim().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(char.ToLowerInvariant(c));
            }
            return builder.ToString();
        }
    }

    // ===== Mensagens da API de login (JSON). Campos públicos: leem igual no Unity (JsonUtility) e no .NET. =====

    [Serializable]
    public sealed class Credentials
    {
        public string Account = "";
        public string Password = "";
    }

    [Serializable]
    public sealed class LoginResponse
    {
        public string Token = "";
        public int ExpiresInSeconds;
    }

    [Serializable]
    public sealed class ApiError
    {
        public string Error = "";
    }

    [Serializable]
    public sealed class CharacterSummary
    {
        public int Id;
        public string Name = "";
        public int ClassId;
        public int Level;
    }

    [Serializable]
    public sealed class CharacterList
    {
        public List<CharacterSummary> Characters = new List<CharacterSummary>();
    }

    [Serializable]
    public sealed class CreateCharacterRequest
    {
        public string Name = "";
        public int ClassId;
    }
}

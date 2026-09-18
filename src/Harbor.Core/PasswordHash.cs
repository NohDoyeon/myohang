using System.Security.Cryptography;

namespace Harbor.Core;

/// <summary>
/// 비밀번호 해시. PBKDF2-SHA256, 계정마다 다른 소금.
/// 평문은 어디에도 남기지 않는다(로그·저장 파일 포함). 검증은 상수 시간 비교.
/// </summary>
public static class PasswordHash
{
    public const int Iterations = 120_000;
    public const int SaltBytes = 16;
    public const int HashBytes = 32;
    public const int MinLength = 4;
    public const int MaxLength = 64;

    public static bool IsAcceptable(string? password)
        => password is not null && password.Length >= MinLength && password.Length <= MaxLength;

    public static string NewSalt() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltBytes));

    public static string Hash(string password, string saltBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return Convert.ToBase64String(key);
    }

    /// <summary>저장된 해시와 대조. 소금·해시가 비어 있으면 false(호출 쪽에서 최초 등록으로 처리).</summary>
    public static bool Verify(string password, string saltBase64, string hashBase64)
    {
        if (string.IsNullOrEmpty(saltBase64) || string.IsNullOrEmpty(hashBase64)) return false;
        try
        {
            var expected = Convert.FromBase64String(hashBase64);
            var salt = Convert.FromBase64String(saltBase64);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }   // 저장 파일이 손상된 경우
    }
}

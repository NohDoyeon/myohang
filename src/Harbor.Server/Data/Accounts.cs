using Harbor.Core;

namespace Harbor.Server.Data;

/// <summary>
/// 닉 + 비밀번호 확인. 웹 로그인과 게임 접속 양쪽이 같은 규칙을 쓰도록 한 곳에 모았다.
///
/// **가입(=account 테이블에 쓰기)은 웹만 한다.** 게임 서버는 확인만 한다.
/// 계정 테이블의 주인을 하나로 못박아야, 웹이 Next.js 로 떨어져 나가도 둘이 같은 행을 두고 다투지 않는다.
/// 그래서 `allowCreate` 는 웹 로그인 경로에서만 true 다.
/// </summary>
public sealed class Accounts
{
    public const int NickMax = 16;

    public enum Result { Ok, BadNick, BadPassword, WrongPassword, NoAccount }

    private readonly SaveStore _save;
    public Accounts(SaveStore save) => _save = save;

    public static string? NickError(string nick)
        => nick.Length is 0 or > NickMax ? $"닉네임은 1~{NickMax}자로 입력해 주세요" : null;

    /// <param name="allowCreate">처음 보는 닉이면 그 자리에서 계정을 만든다. **웹 로그인에서만 true.**</param>
    public Result Authenticate(string nick, string password, out bool created, bool allowCreate = false)
    {
        created = false;
        nick = nick.Trim();
        if (NickError(nick) is not null) return Result.BadNick;
        if (!PasswordHash.IsAcceptable(password)) return Result.BadPassword;

        var acc = _save.GetUser(nick);
        bool needsPassword = acc is null || acc.Salt.Length == 0 || acc.Hash.Length == 0;
        if (!needsPassword && !PasswordHash.Verify(password, acc!.Salt, acc.Hash)) return Result.WrongPassword;

        if (needsPassword)
        {
            if (!allowCreate) return Result.NoAccount;      // 게임에서는 가입할 수 없다 — 웹에서 먼저
            var salt = PasswordHash.NewSalt();
            _save.SetPassword(nick, salt, PasswordHash.Hash(password, salt));
            created = acc is null;
        }
        return Result.Ok;
    }

    public static string Message(Result r) => r switch
    {
        Result.BadNick => $"닉네임은 1~{NickMax}자로 입력해 주세요",
        Result.BadPassword => $"비밀번호는 {PasswordHash.MinLength}~{PasswordHash.MaxLength}자로 입력해 주세요",
        Result.WrongPassword => "비밀번호가 맞지 않아요",
        Result.NoAccount => "가입되지 않은 닉네임이에요. 웹에서 먼저 가입해 주세요",
        _ => "",
    };
}

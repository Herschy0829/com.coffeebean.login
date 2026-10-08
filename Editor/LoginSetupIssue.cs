using System.Collections.Generic;

namespace CoffeeBean.Login.EditorTools
{
    /// <summary>自检项级别。</summary>
    public enum LoginSetupLevel
    {
        /// <summary>信息（不算问题）。</summary>
        Info,

        /// <summary>建议修（不修也能跑，但会在真机上出问题）。</summary>
        Warning,

        /// <summary>必须修（不修功能一定是坏的）。</summary>
        Error,
    }

    /// <summary>一条自检结果：问题 + 怎么修（修法写清楚，别只报错）。</summary>
    public sealed class LoginSetupIssue
    {
        public LoginSetupLevel Level;
        public string Title;
        public string Detail;
        public string Fix;

        public static LoginSetupIssue Error(string title, string detail, string fix)
            => new LoginSetupIssue { Level = LoginSetupLevel.Error, Title = title, Detail = detail, Fix = fix };

        public static LoginSetupIssue Warning(string title, string detail, string fix)
            => new LoginSetupIssue { Level = LoginSetupLevel.Warning, Title = title, Detail = detail, Fix = fix };

        public static LoginSetupIssue Info(string title, string detail)
            => new LoginSetupIssue { Level = LoginSetupLevel.Info, Title = title, Detail = detail, Fix = string.Empty };

        public override string ToString() => "[" + Level + "] " + Title + " —— " + Detail;

        public static int CountErrors(List<LoginSetupIssue> issues)
        {
            int n = 0;
            if (issues == null) return 0;
            foreach (LoginSetupIssue issue in issues)
            {
                if (issue.Level == LoginSetupLevel.Error) n++;
            }
            return n;
        }
    }
}

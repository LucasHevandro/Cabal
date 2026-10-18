namespace Game.Shared.Accounts
{
    /// <summary>
    /// Termos de uso e política de privacidade. Os textos ficam em <c>Resources/Legal/</c> (o Unity lê pelo Resources; o
    /// servidor copia para a pasta Legal/ ao lado do programa e mostra em <c>/api/legal/termos</c> e
    /// <c>/api/legal/privacidade</c>). Ao mudar o texto de forma relevante, troque a <see cref="Version"/>: quem aceitou
    /// a versão antiga precisa aceitar de novo no próximo login.
    /// </summary>
    public static class Legal
    {
        public const string Version = "2026-10-17";
        public const string TermsName = "termos";
        public const string PrivacyName = "privacidade";
    }

    /// <summary>
    /// Relatórios do cliente para as métricas do teste fechado (tutorial, FPS, erros). O servidor só aceita estes tipos,
    /// corta textos longos e ignora o que passar do limite por sessão. O jogador pode desligar nas opções.
    /// </summary>
    public static class ClientReportRules
    {
        public const string Tutorial = "tutorial";
        public const string Fps = "fps";
        public const string Error = "error";

        public const int MaxTextLength = 200;
        public const int MaxPerSession = 120;

        public static bool IsKnown(string kind) => kind == Tutorial || kind == Fps || kind == Error;
    }
}

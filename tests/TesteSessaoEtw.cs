using System;
using Overlay;

// Regressão do bug do OpenTraceA (etapa 5, achado 1): o nome da sessão precisa chegar ao Windows em UTF-16.
// Sem admin, abrir a sessão do sistema "EventLog-Application" deve dar 5 (acesso negado = sessão encontrada), nunca 4201 (não encontrada).
// Como admin a abertura funcionaria e bloquearia consumindo eventos, então o teste é pulado.
static class TesteSessaoEtw
{
    static int Main()
    {
        if (EhAdmin()) { Console.WriteLine("[PULADO] abertura de sessao ETW (rodando como admin)"); return 0; }

        uint codigo = MonitorRede.TestarAbertura("EventLog-Application");
        bool ok = codigo == 5;
        Console.WriteLine((ok ? "[OK]    " : "[FALHA] ") + "OpenTrace acha a sessao pelo nome (obtido " + codigo + ", esperado 5)");
        return ok ? 0 : 1;
    }

    static bool EhAdmin()
    {
        using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}

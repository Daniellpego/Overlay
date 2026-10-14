using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using Overlay;

// Testes do atalho, das cores por faixa, da correção de faixas do config e do XML da tarefa de início automático.
// Compilado e rodado por tests\verificar.ps1 (sem admin).
static class TesteEtapa4
{
    static int falhas;

    static int Main()
    {
        TestarAtalhos();
        TestarFaixas();
        TestarConfigFaixasInvertidas();
        TestarXmlTarefa();

        Console.WriteLine(falhas == 0 ? "Etapa 4: tudo OK" : "Etapa 4: " + falhas + " falha(s)");
        return falhas == 0 ? 0 : 1;
    }

    static void Checar(string nome, bool ok, string detalhe)
    {
        Console.WriteLine((ok ? "[OK]    " : "[FALHA] ") + nome + " " + detalhe);
        if (!ok) falhas++;
    }

    static void Atalho(string texto, bool valido, uint modEsperado, Keys teclaEsperada)
    {
        uint mod, tecla;
        bool ok = Overlay.Atalho.TentarLer(texto, out mod, out tecla);
        bool certo = ok == valido && (!valido || (mod == (modEsperado | Overlay.Atalho.MOD_NOREPEAT) && tecla == (uint)teclaEsperada));
        Checar("atalho '" + texto + "'", certo, "(valido=" + ok + " mod=" + mod + " tecla=" + tecla + ")");
    }

    static void TestarAtalhos()
    {
        Atalho("Ctrl+Shift+F10", true, Overlay.Atalho.MOD_CONTROL | Overlay.Atalho.MOD_SHIFT, Keys.F10);
        Atalho(" alt + o ", true, Overlay.Atalho.MOD_ALT, Keys.O);
        Atalho("Ctrl+5", true, Overlay.Atalho.MOD_CONTROL, Keys.D5);
        Atalho("F9", true, 0, Keys.F9);
        Atalho("Win+Home", true, Overlay.Atalho.MOD_WIN, Keys.Home);
        Atalho("O", false, 0, Keys.None);           // tecla comum sozinha roubaria a tecla do jogo
        Atalho("Ctrl+Shift", false, 0, Keys.None);  // só modificadores
        Atalho("Ctrl+A+B", false, 0, Keys.None);
        Atalho("Ctrl+Banana", false, 0, Keys.None);
        Atalho("Ctrl+42", false, 0, Keys.None);
        Atalho("Ctrl++", false, 0, Keys.None);
        Atalho("", false, 0, Keys.None);
    }

    static void TestarFaixas()
    {
        var c = new Config();
        Checar("fps 144 = bom", Faixas.CorFps(144, c) == c.CorBom, "");
        Checar("fps 60 (limite) = bom", Faixas.CorFps(60, c) == c.CorBom, "");
        Checar("fps 45 = medio", Faixas.CorFps(45, c) == c.CorMedio, "");
        Checar("fps 20 = ruim", Faixas.CorFps(20, c) == c.CorRuim, "");
        Checar("fps sem dado = cor do texto", Faixas.CorFps(null, c) == c.CorTexto, "");
        Checar("ping 20 = bom", Faixas.CorPing(20, c) == c.CorBom, "");
        Checar("ping 60 (limite) = bom", Faixas.CorPing(60, c) == c.CorBom, "");
        Checar("ping 90 = medio", Faixas.CorPing(90, c) == c.CorMedio, "");
        Checar("ping 200 = ruim", Faixas.CorPing(200, c) == c.CorRuim, "");
        c.CoresPorFaixa = false;
        Checar("cores desligadas = cor do texto", Faixas.CorFps(20, c) == c.CorTexto && Faixas.CorPing(200, c) == c.CorTexto, "");
    }

    static void TestarConfigFaixasInvertidas()
    {
        string caminho = Config.Caminho;
        File.WriteAllText(caminho,
            "fps_bom = 30\r\nfps_ruim = 100\r\nping_bom = 80\r\nping_ruim = 200\r\ncor_bom = #00FF00\r\ncor_ruim = verde\r\n");
        try
        {
            Config c = Config.Carregar();
            var padrao = new Config();
            Checar("fps invertido volta ao padrao", c.FpsBom == padrao.FpsBom && c.FpsRuim == padrao.FpsRuim, "(" + c.FpsBom + "/" + c.FpsRuim + ")");
            Checar("ping valido e mantido", c.PingBom == 80 && c.PingRuim == 200, "(" + c.PingBom + "/" + c.PingRuim + ")");
            Checar("cor valida e aplicada", c.CorBom.ToArgb() == Color.FromArgb(0, 255, 0).ToArgb(), "");
            Checar("cor invalida mantem padrao", c.CorRuim == padrao.CorRuim, "");
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    static void TestarXmlTarefa()
    {
        string xml = InicioAutomatico.GerarXml(@"E:\pasta & cia\Overlay.exe", @"PC\Usuário <teste>");
        var doc = new XmlDocument();
        try
        {
            doc.LoadXml(xml.Substring(xml.IndexOf("<Task", StringComparison.Ordinal)));
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");
            Checar("xml da tarefa e valido", true, "");
            Checar("tarefa roda como admin", doc.SelectSingleNode("//t:RunLevel", ns).InnerText == "HighestAvailable", "");
            Checar("tarefa sem limite de 72 h", doc.SelectSingleNode("//t:ExecutionTimeLimit", ns).InnerText == "PT0S", "");
            Checar("tarefa roda na bateria", doc.SelectSingleNode("//t:DisallowStartIfOnBatteries", ns).InnerText == "false", "");
            Checar("caminho com & escapado", doc.SelectSingleNode("//t:Command", ns).InnerText == @"E:\pasta & cia\Overlay.exe", "");
            Checar("pasta de trabalho certa", doc.SelectSingleNode("//t:WorkingDirectory", ns).InnerText == @"E:\pasta & cia", "");
        }
        catch (XmlException ex)
        {
            Checar("xml da tarefa e valido", false, ex.Message);
        }
    }
}

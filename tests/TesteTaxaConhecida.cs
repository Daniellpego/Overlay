using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Overlay;

// Valida o ContadorTela com uma taxa conhecida: uma janelinha no último monitor muda de cor N vezes por segundo
// e o contador mede aquele monitor. Fases: parada (esperado ~0), ~30/s e ~60/s. Não ativa a janela (não rouba foco).
static class TesteTaxaConhecida
{
    class Janela : Form
    {
        public Janela()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Screen[] telas = Screen.AllScreens;
            Rectangle area = telas[telas.Length - 1].Bounds;
            Bounds = new Rectangle(area.Left + 40, area.Bottom - 160, 160, 120);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
    }

    [STAThread]
    static int Main()
    {
        var janela = new Janela();
        janela.Show();
        IntPtr hwnd = janela.Handle;
        int falhas = 0;
        using (var contador = new ContadorTela(() => hwnd))
        {
            contador.Iniciar();
            falhas += Fase(janela, contador, 0, "parada", 0, 12);
            falhas += Fase(janela, contador, 33, "~30/s", 22, 40);
            falhas += Fase(janela, contador, 16, "~60/s", 45, 75);
        }
        janela.Close();
        Console.WriteLine(falhas == 0 ? "Taxa conhecida: tudo OK" : "Taxa conhecida: " + falhas + " falha(s)");
        return falhas == 0 ? 0 : 1;
    }

    // intervaloMs = 0: janela parada. Mede o 4º segundo da fase (os primeiros estabilizam).
    static int Fase(Form janela, ContadorTela contador, int intervaloMs, string nome, int minimo, int maximo)
    {
        var fim = DateTime.UtcNow.AddSeconds(4);
        var proxima = DateTime.UtcNow;
        bool alterna = false;
        while (DateTime.UtcNow < fim)
        {
            if (intervaloMs > 0 && DateTime.UtcNow >= proxima)
            {
                alterna = !alterna;
                janela.BackColor = alterna ? Color.DarkRed : Color.DarkBlue;
                janela.Update();
                proxima = proxima.AddMilliseconds(intervaloMs);
            }
            Application.DoEvents();
            Thread.Sleep(1);
        }
        int? fps = contador.Fps();
        int valor = fps.HasValue ? fps.Value : 0;
        bool ok = valor >= minimo && valor <= maximo;
        Console.WriteLine((ok ? "[OK]    " : "[FALHA] ") + "janela " + nome + ": medido " + valor + " (esperado " + minimo + "-" + maximo + ")" +
            (contador.UltimoErro != null ? " erro: " + contador.UltimoErro : ""));
        return ok ? 0 : 1;
    }
}

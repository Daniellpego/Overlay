using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Overlay;

// Prova do FPS pela tela (ContadorTela) contra um gabarito: uma janela no último monitor troca de cor num ritmo exato
// (relógio de alta precisão) e conta quantas trocas fez de verdade. O ContadorTela, o mesmo código do overlay, mede
// aquele monitor. Junto roda um "overlay falso" que se redesenha 2x/s e se registra como atualização própria, para
// provar o desconto. Antes, uma fase parada mede o ruído de fundo (outras janelas mudando no mesmo monitor).
// Não ativa as janelas (não rouba foco do jogo). Critério: diferença <= 3% ou 1,5 quadro, o que for maior.
static class ProvaFpsTela
{
    class Janela : Form
    {
        public Janela(int x, int y, int largura, int altura)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Screen[] telas = Screen.AllScreens;
            Rectangle area = telas[telas.Length - 1].Bounds;
            Bounds = new Rectangle(area.Left + x, area.Bottom - y, largura, altura);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
    }

    static Janela gabarito, overlayFalso;
    static ContadorTela contador;

    [STAThread]
    static int Main()
    {
        int[] ritmos = { 30, 60, 90, 120, 144 };
        gabarito = new Janela(40, 220, 200, 150);
        overlayFalso = new Janela(260, 120, 120, 30);
        gabarito.Show();
        overlayFalso.Show();
        IntPtr hwnd = gabarito.Handle;
        int falhas = 0;
        using (contador = new ContadorTela(() => hwnd))
        {
            contador.Iniciar();
            double ignorado;
            Rodar(0, 3, null, out ignorado); // aquecimento
            var parado = new List<int>();
            Rodar(0, 6, parado, out ignorado);
            double ruido = Media(parado);
            // O ruído de fundo só soma quadros com a tela parada; com algo se mexendo, essas mudanças entram em composições
            // que já seriam contadas. Por isso compara-se o medido direto com o real (subtrair o ruído corrigiria demais).
            Console.WriteLine("Tela parada (so ruido de fundo, overlay falso descontado): {0:N1} quadros/s", ruido);
            Console.WriteLine("ritmo | trocas reais/s | FPS medido | diferenca");
            foreach (int ritmo in ritmos)
            {
                var amostras = new List<int>();
                double real;
                Rodar(ritmo, 6, amostras, out real);
                double medido = Media(amostras);
                double diferenca = medido - real;
                // Até 3% ou 1,5 quadro (o que for maior): abaixo de ~50 FPS um quadro a mais já passaria de 3%.
                bool ok = Math.Abs(diferenca) <= Math.Max(real * 0.03, 1.5);
                if (!ok) falhas++;
                Console.WriteLine("{0,5} | {1,14:N1} | {2,10:N1} | {3:+0.0;-0.0} ({4:+0.0;-0.0}%) {5}", ritmo, real, medido, diferenca,
                    real > 0 ? diferenca / real * 100 : 0, ok ? "OK" : "FALHA");
            }
        }
        gabarito.Close();
        overlayFalso.Close();
        Console.WriteLine(falhas == 0 ? "Prova FPS pela tela: tudo OK em todos os ritmos" : "Prova FPS pela tela: " + falhas + " falha(s)");
        return falhas == 0 ? 0 : 1;
    }

    static double Media(List<int> valores)
    {
        double soma = 0;
        foreach (int v in valores) soma += v;
        return valores.Count > 0 ? soma / valores.Count : 0;
    }

    // Gabarito troca de cor exatamente "ritmo" vezes/s; overlay falso a cada 500 ms (como o real) e se registra.
    // Os 2 primeiros segundos estabilizam; depois amostra o contador a cada 1 s.
    static void Rodar(int ritmo, int segundos, List<int> amostras, out double taxaReal)
    {
        var relogio = Stopwatch.StartNew();
        double periodoMs = ritmo > 0 ? 1000.0 / ritmo : double.MaxValue;
        double proxima = 0, proximoOverlay = 0;
        long trocas = 0, trocasNoInicioDaMedida = -1;
        double inicioMedidaMs = 2000, proximaAmostraMs = 3000;
        bool alterna = false, alternaOverlay = false;
        while (relogio.Elapsed.TotalMilliseconds < segundos * 1000)
        {
            double agora = relogio.Elapsed.TotalMilliseconds;
            if (agora >= proxima)
            {
                alterna = !alterna;
                gabarito.BackColor = alterna ? Color.DarkRed : Color.DarkBlue;
                gabarito.Update();
                trocas++;
                proxima += periodoMs;
                if (proxima < agora) proxima = agora + periodoMs; // atrasou: não acumula rajada
            }
            if (agora >= proximoOverlay)
            {
                alternaOverlay = !alternaOverlay;
                overlayFalso.BackColor = alternaOverlay ? Color.DarkGreen : Color.Black;
                overlayFalso.Update();
                contador.RegistrarAtualizacaoPropria(overlayFalso.Handle);
                proximoOverlay += 500;
            }
            if (agora >= inicioMedidaMs && trocasNoInicioDaMedida < 0) trocasNoInicioDaMedida = trocas;
            if (amostras != null && agora >= proximaAmostraMs)
            {
                int? fps = contador.Fps();
                amostras.Add(fps.HasValue ? fps.Value : 0);
                proximaAmostraMs += 1000;
            }
            Application.DoEvents();
        }
        double duracaoMedida = (relogio.Elapsed.TotalMilliseconds - inicioMedidaMs) / 1000.0;
        taxaReal = (trocas - trocasNoInicioDaMedida) / duracaoMedida;
    }
}

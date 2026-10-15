using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Overlay;

// Prova do FPS pela tela (ContadorTela) contra um gabarito, no último monitor, sem roubar foco:
// - "jogo": janela que troca de cor num ritmo exato (relógio de alta precisão) e conta as trocas de verdade;
// - "overlay falso": dentro do canto do jogo, se redesenha 2x/s (como o real) e é a área ignorada;
// - "ruído": outra janela ao lado mudando 50x/s (como um Discord ou vídeo aberto ao lado do jogo).
// O ContadorTela (mesmo código do overlay) mede o "jogo". Critério: diferença <= 3% ou 1,5 quadro (o que for maior),
// e com o jogo parado tem que dar ~0 mesmo com o ruído e o overlay se mexendo.
static class ProvaFpsTela
{
    class Janela : Form
    {
        public Janela(int x, int y, int largura, int altura)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Screen[] telas = Screen.AllScreens;
            Rectangle area = telas[telas.Length - 1].Bounds;
            Bounds = new Rectangle(area.Left + x, area.Bottom - y, largura, altura);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
    }

    static Janela jogo, overlayFalso, ruido;
    static ContadorTela contador;

    [STAThread]
    static int Main()
    {
        int[] ritmos = { 30, 60, 90, 120, 144 };
        jogo = new Janela(40, 260, 240, 180);
        ruido = new Janela(320, 260, 160, 120);
        overlayFalso = new Janela(40 + 240 - 110, 260 - 6, 100, 24); // canto superior direito do "jogo"
        jogo.Show();
        ruido.Show();
        overlayFalso.Show();
        IntPtr hwnd = jogo.Handle;
        int falhas = 0;
        using (contador = new ContadorTela(() => hwnd))
        {
            contador.DefinirAreaIgnorada(overlayFalso.Bounds);
            contador.Iniciar();
            double ignorado;
            Rodar(0, 3, null, out ignorado); // aquecimento
            var parado = new List<int>();
            Rodar(0, 6, parado, out ignorado);
            double mediaParado = Media(parado);
            bool paradoOk = mediaParado <= 1;
            if (!paradoOk) falhas++;
            Console.WriteLine("Jogo parado (ruido ao lado 50/s + overlay 2/s): {0:N1} quadros/s {1}", mediaParado, paradoOk ? "OK" : "FALHA");
            Console.WriteLine("ritmo | trocas reais/s | FPS medido | diferenca");
            foreach (int ritmo in ritmos)
            {
                var amostras = new List<int>();
                double real;
                Rodar(ritmo, 6, amostras, out real);
                double medido = Media(amostras);
                double diferenca = medido - real;
                bool ok = Math.Abs(diferenca) <= Math.Max(real * 0.03, 1.5);
                if (!ok) falhas++;
                Console.WriteLine("{0,5} | {1,14:N1} | {2,10:N1} | {3:+0.0;-0.0} ({4:+0.0;-0.0}%) {5}", ritmo, real, medido, diferenca,
                    real > 0 ? diferenca / real * 100 : 0, ok ? "OK" : "FALHA");
            }
        }
        jogo.Close();
        ruido.Close();
        overlayFalso.Close();
        Console.WriteLine(falhas == 0 ? "Prova FPS pela tela: tudo OK" : "Prova FPS pela tela: " + falhas + " falha(s)");
        return falhas == 0 ? 0 : 1;
    }

    static double Media(List<int> valores)
    {
        double soma = 0;
        foreach (int v in valores) soma += v;
        return valores.Count > 0 ? soma / valores.Count : 0;
    }

    static void Trocar(Form janela, ref bool alterna, Color a, Color b)
    {
        alterna = !alterna;
        janela.BackColor = alterna ? a : b;
        janela.Update();
    }

    // Os 2 primeiros segundos estabilizam; depois amostra o contador a cada 1 s.
    static void Rodar(int ritmo, int segundos, List<int> amostras, out double taxaReal)
    {
        var relogio = Stopwatch.StartNew();
        double periodoMs = ritmo > 0 ? 1000.0 / ritmo : double.MaxValue;
        double proxima = 0, proximoOverlay = 0, proximoRuido = 0;
        long trocas = 0, trocasNoInicioDaMedida = -1;
        double inicioMedidaMs = 2000, proximaAmostraMs = 3000;
        bool a1 = false, a2 = false, a3 = false;
        while (relogio.Elapsed.TotalMilliseconds < segundos * 1000)
        {
            double agora = relogio.Elapsed.TotalMilliseconds;
            if (agora >= proxima)
            {
                Trocar(jogo, ref a1, Color.DarkRed, Color.DarkBlue);
                trocas++;
                proxima += periodoMs;
                if (proxima < agora) proxima = agora + periodoMs; // atrasou: não acumula rajada
            }
            if (agora >= proximoOverlay) { Trocar(overlayFalso, ref a2, Color.DarkGreen, Color.Black); proximoOverlay += 500; }
            if (agora >= proximoRuido) { Trocar(ruido, ref a3, Color.Orange, Color.Purple); proximoRuido += 20; }
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

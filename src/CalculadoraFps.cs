using System;
using System.Collections.Generic;
using System.Globalization;

namespace Overlay
{
    // Lógica pura (sem processo nem janela) para poder ser testada com CSV sintético em tests\TesteCalculadoraFps.cs.
    // Usa só CPUStartTime: os intervalos entre inícios de frame são os intervalos entre Present(), igual ao "FPS" dos overlays comuns.
    class CalculadoraFps
    {
        public const double JanelaMs = 1000;
        public const double ValidadeMs = 2000;

        readonly object trava = new object();
        readonly Dictionary<string, Queue<double>> framesPorSwapchain = new Dictionary<string, Queue<double>>();
        int indicePid = -1;
        int indiceSwapchain = -1;
        int indiceTempo = -1;
        int maiorIndice = -1;
        int pidAlvo;
        double ultimoTempoFrame;
        double ultimaChegadaMs = double.MinValue;

        public string ErroCabecalho { get; private set; }

        public void DefinirAlvo(int pid)
        {
            lock (trava)
            {
                if (pid == pidAlvo) return;
                pidAlvo = pid;
                framesPorSwapchain.Clear();
                ultimoTempoFrame = 0;
                ultimaChegadaMs = double.MinValue;
            }
        }

        public void ProcessarLinha(string linha, double agoraMs)
        {
            if (string.IsNullOrEmpty(linha)) return;

            if (linha.StartsWith("Application,", StringComparison.Ordinal))
            {
                LerCabecalho(linha.Split(','));
                return;
            }
            if (maiorIndice < 0) return;

            string[] campos = linha.Split(',');
            if (campos.Length <= maiorIndice) return;

            int pid;
            if (!int.TryParse(campos[indicePid], NumberStyles.Integer, CultureInfo.InvariantCulture, out pid)) return;

            lock (trava)
            {
                if (pid != pidAlvo || pid == 0) return;

                double tempo;
                if (!double.TryParse(campos[indiceTempo], NumberStyles.Float, CultureInfo.InvariantCulture, out tempo) ||
                    double.IsNaN(tempo) || double.IsInfinity(tempo) || tempo < 0) return;

                Queue<double> fila;
                string swapchain = campos[indiceSwapchain];
                if (!framesPorSwapchain.TryGetValue(swapchain, out fila))
                {
                    fila = new Queue<double>();
                    framesPorSwapchain[swapchain] = fila;
                }
                fila.Enqueue(tempo);
                while (fila.Count > 0 && fila.Peek() < tempo - JanelaMs) fila.Dequeue();

                if (tempo > ultimoTempoFrame) ultimoTempoFrame = tempo;
                ultimaChegadaMs = agoraMs;
            }
        }

        // null = sem dados recentes do processo em primeiro plano (desktop, jogo em loading longo, PresentMon parado).
        public int? Fps(double agoraMs)
        {
            lock (trava)
            {
                if (agoraMs - ultimaChegadaMs > ValidadeMs) return null;

                // Um processo pode ter várias swapchains (launcher, overlay de terceiros); vale a que mais apresentou frames.
                double melhor = -1;
                int maiorAmostra = 0;
                foreach (Queue<double> fila in framesPorSwapchain.Values)
                {
                    if (fila.Count < 3) continue;
                    double primeiro = fila.Peek();
                    double ultimo = primeiro;
                    foreach (double t in fila) ultimo = t;

                    // Swapchain que parou de apresentar (fechou) não pode congelar o valor antigo.
                    if (ultimo < ultimoTempoFrame - JanelaMs) continue;
                    // Uma rajada curta de uma superfície auxiliar não representa o ritmo do jogo.
                    if (ultimo - primeiro < 250) continue;

                    double fps = (fila.Count - 1) * 1000.0 / (ultimo - primeiro);
                    if (fila.Count > maiorAmostra)
                    {
                        maiorAmostra = fila.Count;
                        melhor = fps;
                    }
                }
                if (melhor < 0) return null;
                return (int)Math.Round(melhor);
            }
        }

        void LerCabecalho(string[] colunas)
        {
            int pid = Array.IndexOf(colunas, "ProcessID");
            int swapchain = Array.IndexOf(colunas, "SwapChainAddress");
            int tempo = Array.IndexOf(colunas, "CPUStartTime");

            lock (trava)
            {
                if (pid < 0 || swapchain < 0 || tempo < 0)
                {
                    maiorIndice = -1;
                    ErroCabecalho = "Colunas esperadas ausentes no CSV do PresentMon: " + string.Join(",", colunas);
                    return;
                }
                indicePid = pid;
                indiceSwapchain = swapchain;
                indiceTempo = tempo;
                maiorIndice = Math.Max(pid, Math.Max(swapchain, tempo));
                ErroCabecalho = null;
                framesPorSwapchain.Clear();
                ultimoTempoFrame = 0;
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using Overlay;

static class TesteRegressoes
{
    static int falhas;

    static void Checar(string nome, bool ok)
    {
        Console.WriteLine((ok ? "[OK] " : "[FALHA] ") + nome);
        if (!ok) falhas++;
    }

    static int Main()
    {
        PingNaoMisturaDestinos();
        CapturaRecusaIncerteza();
        ValidacaoExigeDados();
        PosicaoPermaneceVisivel();
        DestinoPrecisaEstabilidade();
        Console.WriteLine("Regressoes: " + falhas + " falha(s)");
        return falhas == 0 ? 0 : 1;
    }

    static void PingNaoMisturaDestinos()
    {
        using (var m = new MedidorPing("1.1.1.1", null))
        {
            IPAddress a = IPAddress.Parse("1.1.1.1"), b = IPAddress.Parse("8.8.8.8");
            for (int i = 0; i < 3; i++) m.Registrar(OrigemPing.ServidorDoJogo, a, 10);
            m.Registrar(OrigemPing.ServidorDoJogo, b, 100);
            int? ms; OrigemPing origem;
            m.Ler(out ms, out origem);
            Checar("novo servidor usa apenas amostras do novo IP", ms == 100 && m.DestinoAtual.Equals(b));
            m.Registrar(OrigemPing.ServidorDoJogo, a, null);
            m.Ler(out ms, out origem);
            Checar("novo servidor sem resposta nao herda ping anterior", !ms.HasValue);
            m.Registrar(OrigemPing.Reserva, a, 10);
            m.Registrar(OrigemPing.Reserva, b, 150);
            m.Ler(out ms, out origem);
            Checar("troca do alvo de reserva tambem limpa historico", ms == 150);
        }
    }

    static void CapturaRecusaIncerteza()
    {
        using (var c = new ContadorTela())
        {
            long fim = Stopwatch.GetTimestamp(), f = Stopwatch.Frequency;
            for (int i = 30; i >= 0; i--) c.RegistrarAmostra(fim - i * f / 60, 1, false, true);
            Checar("quadros isolados mantem 60 FPS", c.Fps() == 60);
            c.RegistrarAmostra(fim, 10, false, true);
            Checar("acumulados do desktop nao inflam FPS", c.Fps() == 60);
            c.RegistrarAmostra(fim, 1, true, true);
            Checar("retangulos agrupados sinalizam incerteza sem inflar FPS", c.Fps() == 60 && c.UltimoErro != null);
        }
        using (var c = new ContadorTela())
        {
            long fim = Stopwatch.GetTimestamp(), f = Stopwatch.Frequency;
            for (int i = 54; i >= 35; i--) c.RegistrarAmostra(fim - i * f / 60, 1, false, true);
            c.RegistrarAmostra(fim - 30 * f / 60, 5, false, true);
            for (int i = 19; i >= 0; i--) c.RegistrarAmostra(fim - i * f / 60, 1, false, true);
            Checar("intervalo incerto entre duas sequencias nao altera 60 FPS", c.Fps() == 60);
        }
        Rectangle jogo = new Rectangle(0, 0, 500, 500), overlay = new Rectangle(400, 0, 100, 30);
        Checar("redesenho restrito ao overlay e ignorado", !ContadorTela.Relevante(overlay, jogo, overlay));
        Checar("mudanca fora do jogo e ignorada", !ContadorTela.Relevante(new Rectangle(600, 0, 30, 30), jogo, overlay));
        bool incerta = false;
        Checar("invalidacao global do compositor nao vira FPS", !ContadorTela.ClassificarMudanca(new Rectangle(0, 0, 1920, 1080), jogo, overlay, ref incerta) && incerta);
    }

    static void ValidacaoExigeDados()
    {
        Checar("sem dados nao aprova", !AvaliacaoProva.AmostrasSuficientes(0, 30));
        Checar("poucos dados nao aprovam", !AvaliacaoProva.AmostrasSuficientes(5, 30));
        Checar("80 por cento de amostras permite comparar", AvaliacaoProva.AmostrasSuficientes(24, 30));
        Checar("FPS acima do teto tambem reprova", !AvaliacaoProva.FpsCompativel(300, 1000, 240));
        Checar("mais de cinco por cento em FPS baixo reprova", !AvaliacaoProva.FpsCompativel(30, 32, 240));
        Checar("FPS limitado pelo monitor e aceito", AvaliacaoProva.FpsCompativel(300, 240, 240));
        Checar("falha prevalece sobre inconclusao", AvaliacaoProva.CodigoResultado(1, 1) == 1);
        Checar("inconclusao tem codigo proprio", AvaliacaoProva.CodigoResultado(0, 1) == 2);
        Checar("aprovacao exige verificacoes completas", AvaliacaoProva.CodigoResultado(0, 0) == 0);
    }

    static void PosicaoPermaneceVisivel()
    {
        var c = new Config { MargemX = 5000, MargemY = 5000 };
        Rectangle area = new Rectangle(-1920, 0, 1920, 1080);
        foreach (string canto in new[] { "superior-direito", "superior-esquerdo", "inferior-direito", "inferior-esquerdo" })
        {
            c.Canto = canto;
            Size tamanho = new Size(400, 40);
            Point ponto = OverlayForm.LimitarPosicao(area, tamanho, c);
            Checar("margens extremas preservam janela: " + canto, area.Contains(new Rectangle(ponto, tamanho)));
        }
    }

    static void DestinoPrecisaEstabilidade()
    {
        var d = new DetectorServidor();
        d.DefinirAlvo(100);
        IPAddress a = IPAddress.Parse("52.1.2.3"), b = IPAddress.Parse("34.1.2.3");
        for (int i = 0; i < 60; i++) d.RegistrarEnvio(100, a, i * 50);
        Checar("candidato novo nao e anunciado imediatamente", d.Servidor(3000) == null);
        Checar("lider estavel vira destino provavel", a.Equals(d.Servidor(5000)));
        for (int i = 0; i < 60; i++) d.RegistrarEnvio(100, b, 2000 + i * 50);
        Checar("destinos empatados nao sao anunciados como servidor", d.Servidor(5000) == null);
        d.DefinirAlvo(200);
        Checar("trocar processo limpa a confianca", d.Servidor(5000) == null);
    }
}

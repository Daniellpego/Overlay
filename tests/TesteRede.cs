using System;
using System.Net;
using System.Threading;
using Overlay;

// Testes do DetectorServidor (lógica pura) e do MedidorPing (ping ICMP real, não precisa de admin).
// Compilado e rodado por tests\verificar.ps1.
static class TesteRede
{
    static int falhas;

    static int Main()
    {
        AchaServidorMaisUsado();
        IgnoraIpsLocais();
        PoucoTrafegoNaoEhPartida();
        IgnoraOutrosProcessos();
        ServidorSomeQuandoTrafegoPara();
        TrocaDeJanelaLimpa();
        PingNoAlvoReserva();
        ServidorMudoCaiParaReserva();
        AlvoInvalidoNaoDerruba();

        Console.WriteLine(falhas == 0 ? "Rede: tudo OK" : "Rede: " + falhas + " falha(s)");
        return falhas == 0 ? 0 : 1;
    }

    static void Checar(string nome, bool ok, string detalhe)
    {
        Console.WriteLine((ok ? "[OK]    " : "[FALHA] ") + nome + " " + detalhe);
        if (!ok) falhas++;
    }

    static void Enviar(DetectorServidor d, int pid, string ip, int pacotesPorSegundo, double inicioMs, double duracaoMs)
    {
        double passo = 1000.0 / pacotesPorSegundo;
        for (double t = inicioMs; t < inicioMs + duracaoMs; t += passo)
            d.RegistrarEnvio(pid, IPAddress.Parse(ip), t);
    }

    static void AchaServidorMaisUsado()
    {
        var d = new DetectorServidor();
        d.DefinirAlvo(100);
        Enviar(d, 100, "52.1.2.3", 60, 0, 5000);    // servidor da partida
        Enviar(d, 100, "8.8.8.8", 1, 0, 5000);      // DNS
        Enviar(d, 100, "34.9.9.9", 10, 0, 5000);    // chat de voz
        d.Servidor(5000); // inicia a observação do candidato; exige 2 s de liderança
        IPAddress s = d.Servidor(7000);
        Checar("acha o IP com mais pacotes", s != null && s.ToString() == "52.1.2.3", "(obtido " + s + ")");
    }

    static void IgnoraIpsLocais()
    {
        var d = new DetectorServidor();
        d.DefinirAlvo(100);
        Enviar(d, 100, "192.168.0.1", 200, 0, 5000);
        Enviar(d, 100, "10.0.0.5", 200, 0, 5000);
        Enviar(d, 100, "239.255.255.250", 200, 0, 5000);
        Enviar(d, 100, "fe80::1", 200, 0, 5000);
        Enviar(d, 100, "2804:14c::1", 40, 0, 5000);
        d.Servidor(5000);
        IPAddress s = d.Servidor(7000);
        Checar("ignora LAN/multicast e aceita IPv6 publico", s != null && s.ToString() == "2804:14c::1", "(obtido " + s + ")");
    }

    static void PoucoTrafegoNaoEhPartida()
    {
        var d = new DetectorServidor();
        d.DefinirAlvo(100);
        Enviar(d, 100, "52.1.2.3", 2, 0, 5000);
        d.Servidor(5000);
        IPAddress s = d.Servidor(7000);
        Checar("menu (pouco trafego) nao vira servidor", s == null, "(obtido " + s + ")");
    }

    static void IgnoraOutrosProcessos()
    {
        var d = new DetectorServidor();
        d.DefinirAlvo(100);
        Enviar(d, 999, "142.250.1.1", 500, 0, 5000); // navegador baixando algo
        Enviar(d, 100, "52.1.2.3", 30, 0, 5000);
        d.Servidor(5000);
        IPAddress s = d.Servidor(7000);
        Checar("ignora trafego de outros processos", s != null && s.ToString() == "52.1.2.3", "(obtido " + s + ")");
    }

    static void ServidorSomeQuandoTrafegoPara()
    {
        var d = new DetectorServidor();
        d.DefinirAlvo(100);
        Enviar(d, 100, "52.1.2.3", 60, 0, 5000);
        IPAddress s = d.Servidor(5000 + DetectorServidor.JanelaMs + 1);
        Checar("partida acabou: servidor some", s == null, "(obtido " + s + ")");
    }

    static void TrocaDeJanelaLimpa()
    {
        var d = new DetectorServidor();
        d.DefinirAlvo(100);
        Enviar(d, 100, "52.1.2.3", 60, 0, 5000);
        d.DefinirAlvo(200);
        IPAddress s = d.Servidor(5000);
        Checar("trocar de janela nao reaproveita servidor", s == null, "(obtido " + s + ")");
    }

    static int? EsperarPing(MedidorPing m, int segundos, out OrigemPing origem)
    {
        int? ms = null;
        origem = OrigemPing.Nenhuma;
        for (int i = 0; i < segundos * 4 && !ms.HasValue; i++)
        {
            Thread.Sleep(250);
            m.Ler(out ms, out origem);
        }
        return ms;
    }

    static void PingNoAlvoReserva()
    {
        using (var m = new MedidorPing("1.1.1.1", null))
        {
            m.Iniciar();
            OrigemPing origem;
            int? ms = EsperarPing(m, 5, out origem);
            Checar("ping real no 1.1.1.1", ms.HasValue && origem == OrigemPing.Reserva, "(obtido " + ms + " ms, " + origem + ")");
        }
    }

    static void ServidorMudoCaiParaReserva()
    {
        // 192.0.2.1 (TEST-NET-1) nunca responde: simula servidor de jogo que bloqueia ping.
        using (var m = new MedidorPing("1.1.1.1", () => IPAddress.Parse("192.0.2.1")))
        {
            m.Iniciar();
            OrigemPing origem;
            int? ms = EsperarPing(m, 10, out origem);
            Checar("servidor que bloqueia ping cai para a reserva", ms.HasValue && origem == OrigemPing.Reserva, "(obtido " + ms + " ms, " + origem + ")");
        }
    }

    static void AlvoInvalidoNaoDerruba()
    {
        using (var m = new MedidorPing("isso.nao.existe.invalid", null))
        {
            m.Iniciar();
            Thread.Sleep(2500);
            int? ms;
            OrigemPing origem;
            m.Ler(out ms, out origem);
            Checar("alvo_ping invalido mostra --- sem travar", !ms.HasValue, "(obtido " + ms + ")");
        }
    }
}

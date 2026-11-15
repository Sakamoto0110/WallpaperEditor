# ADR 0006 — `MediaPlayer` do Windows + DirectComposition no modo animado

**Status:** Aceita para o experimento; A1 e A3 confirmados em 2026-11-07, A2 pendente · **Data:** 2026-11-06

**[Medido]** A reprodução funcionou como descrita, a 30 fps, sem nenhum erro na cópia de quadros. A única diferença em
relação ao relato de terceiro está na anexação: nesta build é preciso enviar `0x052C`. Ver
[04 — Integração com o Windows](../04-integracao-windows.md#como-anexar-na-topologia-elevada).

## Contexto

O modo animado precisa reproduzir `.mp4` atrás dos ícones. Duas restrições definem a escolha:

1. **Medir VRAM é o objetivo.** Quanto menos camadas entre o vídeo e a GPU, mais o número medido
   representa o custo real de um wallpaper em vídeo, e não o de um framework.
2. **A topologia desta máquina.** Na topologia elevada, só o conteúdo apresentado por DirectComposition
   com swap chain flip-model é composto; GDI, blt-model e janelas em camadas não aparecem
   **[Terceiro]**.

## Decisão

Um processo **sem interface**, que usa:

- **`Windows.Media.Playback.MediaPlayer`** em modo *frame server*
  (`IsVideoFrameServerEnabled = true`), com `IsLoopingEnabled`, para decodificar via hardware.
- **Swap chain flip-model + DirectComposition** para apresentar, com a cópia de quadro feita por
  `CopyFrameToVideoSurface` direto no back buffer, sem textura intermediária, que custaria mais VRAM.
- **Vortice.Windows** para as interfaces de Direct3D 11, DXGI e DirectComposition em C#.

## Por quê

- **Já vem no Windows.** O `.mp4` é o único formato exigido, e H.264 funciona sem instalar nada.
  HEVC e AV1 usam as extensões já instaladas nesta máquina **[Medido]**.
- **Tem precedente na mesma topologia.** O projeto FeatherWall relata esta combinação funcionando na
  build 26200 **[Terceiro]**. É a única combinação com relato direto nessa topologia que encontramos.
- **Acessível do C#,** sem biblioteca nativa, porque o `MediaPlayer` é uma API WinRT.

## Alternativas

| Opção | Por que não |
|---|---|
| WPF `MediaElement` | Carrega o WPF inteiro para o processo, o que contamina a medição. Além disso, renderiza por uma infraestrutura que não temos como garantir que seja composta na topologia elevada |
| Media Foundation direto (`IMFMediaEngine`) | Controle total, com mais código. **É o plano B** se o `MediaPlayer` limitar algo; a API é parecida |
| libmpv ou LibVLC | Boas opções e fáceis de integrar, mas acrescentam dependências grandes, e o custo medido passaria a incluir decisões de outro player. Podem servir de comparação, se o resultado parecer anormal |
| Navegador ou WebView2 | Consumo muito maior. Mediria o navegador, e não o vídeo |

## Consequências

- O projeto precisa de um target framework com versão do Windows (`net10.0-windows10.0.19041.0`).
- O desenho segue o relato de um único projeto de terceiro. Se o teste A1 falhar, volta-se à análise,
  e este ADR é atualizado.
- A variação de VRAM de outras abordagens fica fora do escopo inicial. Uma comparação com o plano B só
  se justifica se o resultado for muito acima da hipótese.
- O código de anexação suporta apenas a topologia elevada, a única testável aqui. O Windows 10 e
  versões anteriores do Windows 11 não são suportados no modo animado.

# ADR 0006 — `MediaPlayer` do Windows + DirectComposition no modo animado

**Status:** Proposta, a confirmar pelos testes A1 a A3 · **Data:** 2026-11-06

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
  `CopyFrameToVideoSurface`.
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

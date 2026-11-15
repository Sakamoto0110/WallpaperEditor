# 05 — Experimento do modo animado

## A pergunta

> **Quanta VRAM um wallpaper em vídeo consome nesta máquina, e isso compensa?**

O entregável deste experimento é um **relatório com números e uma decisão**, e não uma
funcionalidade pronta. Se a resposta for "não compensa", o projeto `AnimatedHost` é removido, e o
resto do app segue igual.

O modo animado é a única exceção à [regra de leveza](01-visao-do-produto.md#extremamente-leve) do
produto, já que exige um processo rodando e consome GPU o tempo todo. Mesmo aprovado, continua opcional.

## Primeiro: dá para fazer?

Antes de medir qualquer coisa, é preciso provar que a janela aparece atrás dos ícones nesta build.
Não há API oficial para isso, e a única descrição do procedimento vem de um projeto de terceiro
(ver [04 — Integração com o Windows](04-integracao-windows.md#modo-animado-a-janela-atrás-dos-ícones)).

Por isso o experimento começa com testes de viabilidade, A1 a A3 do
[roadmap](06-roadmap.md#testes-de-validação). Uma falha nessa etapa encerra o experimento sem
custo adicional.

## Hipóteses

**[Hipótese]** Estimativa teórica por monitor, antes de medir, para termos com o que comparar.

| Componente | 2560×1080 | 1920×1080 ou 1080×1920 |
|---|---|---|
| Um buffer BGRA da swap chain (4 bytes/pixel) | 10,5 MiB | 7,9 MiB |
| Swap chain de 2 a 3 buffers | 21 a 32 MiB | 16 a 24 MiB |
| Uma superfície decodificada NV12 (1,5 byte/pixel) | 4,0 MiB | 3,0 MiB |
| Pool do decodificador, 8 a 20 superfícies | 32 a 80 MiB | 24 a 60 MiB |
| **Total esperado** | **53 a 112 MiB** | **40 a 84 MiB** |

| # | Hipótese | Como será refutada |
|---|---|---|
| H1 | Um monitor ultrawide em H.264 fica abaixo de 150 MiB, somando todos os processos | Medição acima disso |
| H2 | O consumo depende mais da resolução e do codec que do bitrate | Variar o bitrate muda mais que variar a resolução |
| H3 | Pausar o vídeo **não** libera a VRAM do decodificador; só encerrar o processo libera | Queda significativa ao pausar |
| H4 | O consumo de memória cresce de forma aproximadamente linear com o número de monitores | Três monitores custam muito mais que a soma individual |
| H5 | O vídeo com decodificação por hardware custa menos de 5% de CPU | Medição |

A estimativa de superfícies é um chute informado: alinhamento de memória e o driver da AMD podem
mudar bastante esse número. O motivo de escrevê-la é poder perceber se a medição surpreender.

## O que medir

O número que importa é a **variação total no adaptador**. A medição por processo serve para explicar o resultado, mas
não deve ser somada: **[Medido]** o `dwm` sobe 17 MiB, e o adaptador sobe só o valor do host.

| Métrica | Fonte |
|---|---|
| VRAM total da GPU | `\GPU Adapter Memory(*)\Dedicated Usage` **[Medido: contador disponível]** |
| VRAM por processo (`AnimatedHost`, `dwm`, `explorer`) | `\GPU Process Memory(pid_*)\Dedicated Usage` e `Shared Usage` |
| Uso do decodificador e 3D | `\GPU Engine(pid_*engtype_Video*)\Utilization Percentage` e o equivalente de `3D`. **[Medido]** Nesta GPU, o motor se chama `Video Codec Engine` |
| CPU e memória do host | `\Process(*)\% Processor Time`, `Private Bytes` |
| Fluidez | Quadros descartados, informados pelo próprio host |
| Consumo de energia da GPU | Se o software da AMD expuser de forma simples. Opcional |

Evite o `AdapterRAM` do WMI (limitado a 4 GB) e `IDXGIAdapter3::QueryVideoMemoryInfo` para a
medição principal, porque ele reporta só o processo que chama.

## Protocolo

O script de coleta, em `scripts/`, é versionado e gera um CSV por execução, para que a medição
possa ser refeita.

1. **Condição controlada:** reiniciar, esperar o sistema estabilizar, fechar navegador, jogos e
   editores. Mesmo layout de janelas em todas as execuções. Isso é obrigatório: na medição da troca de
   wallpaper, a VRAM total variou mais de 500 MiB só com o uso normal do PC **[Medido]**.
2. **Linha de base:** 5 minutos sem o host, amostrando a cada segundo.
3. **Carga:** 10 minutos com o host, desconsiderando o primeiro minuto de aquecimento.
4. **Pausa:** 2 minutos com o vídeo pausado (H3).
5. **Encerramento:** 3 minutos após fechar o host, para ver se a VRAM volta à linha de base. Se não
   voltar, há um vazamento a investigar.
6. Registrar mediana e percentil 95 de cada métrica, além da diferença em relação à linha de base.

### Matriz de cenários

Em vez de todas as combinações, um cenário de referência e variações de um fator por vez:

| Cenário | Monitor | Resolução do vídeo | Codec | FPS | Bitrate |
|---|---|---|---|---|---|
| **R — referência** | Ultrawide | 2560×1080 | H.264 | 30 | 10 Mbps |
| C1 / C2 | Ultrawide | 2560×1080 | HEVC / AV1 | 30 | 10 Mbps |
| F1 | Ultrawide | 2560×1080 | H.264 | 60 | 20 Mbps |
| B1 | Ultrawide | 2560×1080 | H.264 | 30 | 40 Mbps |
| M1 | Full HD | 1920×1080 | H.264 | 30 | 10 Mbps |
| M2 | Vertical | 1080×1920 | H.264 | 30 | 10 Mbps |
| S1 | Ultrawide | 3840×1620, reduzido na tela | H.264 | 30 | 25 Mbps |
| T1 | Os 3 monitores ao mesmo tempo | nativa de cada um | H.264 | 30 | 10 Mbps |

Os vídeos de teste vêm de **uma mesma fonte**, para a comparação ser justa: um gerador determinístico embutido no host,
codificado pelo próprio Windows (`scripts/make-test-videos.ps1`), sem instalar nada. O AV1 (C2) fica de fora, porque a
API usada não oferece esse perfil de codificação. O `.mp4` é só o contêiner: o host reporta o codec de verdade no log.

## Critério de decisão

Valores **propostos**, a ajustar antes de medir, para não serem escolhidos depois de conhecer os
resultados:

| Critério | Aprovado se | Para |
|---|---|---|
| Viabilidade | Testes A1 a A3 passam | Funciona nesta build |
| VRAM | Cenário R ≤ 150 MiB | Cerca de 2% dos 8 GB da RX 7600 |
| CPU | Cenário R ≤ 5% | Não atrapalha o uso normal |
| Estabilidade | 1 hora sem vazamento, e sobrevive a reiniciar o Explorer | Dá para deixar ligado |

| Resultado | Decisão |
|---|---|
| Passa em tudo | Promover: adicionar à interface, pausa automática e inicialização com o Windows |
| Viável, mas caro | Manter experimental, atrás de um aviso |
| Inviável | Remover `AnimatedHost`, mantendo este relatório como registro |

## Fora do experimento

Enquanto for experimento, **não** serão construídos: pausa automática ao abrir um jogo, início com
o Windows, galeria de vídeos, áudio ou suporte a outros contêineres. Se o experimento for aprovado,
a pausa automática vira o primeiro requisito, porque um vídeo decodificando durante um jogo gasta
justamente o recurso que este experimento quer medir.

## Resultados

**Preliminares.** **[Medido em 2026-11-07]** Medição reduzida, com o PC em uso e sem a condição controlada do protocolo:
fases de 12 a 20 s de linha de base e de 20 a 40 s de carga, em build Release, sempre no monitor vertical (1080×1920),
onde os testes podem rodar. Um vídeo de outro formato aparece esticado. Ainda falta rodar a matriz na condição
controlada, no monitor certo de cada cenário.

| Vídeo | Resolução | Codec | FPS | Bitrate | Host | **Adaptador** | CPU do host | Decodificador |
|---|---|---|---|---|---|---|---|---|
| M2 | 1080×1920 | H.264 | 30 | 10 Mbps | 137,4 MiB | **+134 MiB** | 0,2% | não lido |
| VF1 | 1080×1920 | H.264 | **60** | 20 Mbps | 137,4 MiB | **+137 MiB** | 0,3% | não lido |
| VB1 | 1080×1920 | H.264 | 30 | **40 Mbps** | 137,4 MiB | **+137 MiB** | 0,3% | não lido |
| VC1 | 1080×1920 | **HEVC** | 30 | 10 Mbps | **68,1 MiB** | **+68 MiB** | 0,3% | não lido |
| M1 | **1920×1080** | H.264 | 30 | 10 Mbps | 133,1 MiB | **+137 MiB** | 0,1% | 7,5% |
| R | **2560×1080** | H.264 | 30 | 10 Mbps | 163,1 MiB | **+161 MiB** | 0,1% | 8,5% |

O vídeo manteve 30 ou 60 fps em todos os casos, exceto o primeiro intervalo de 10 s (29 fps). A memória privada do host
ficou entre 99 e 152 MiB, além da VRAM.

**Não some os processos.** O `dwm` sobe 17 MiB em todos os cenários, mas o total do adaptador sobe só o valor do host. O
valor bate com os dois buffers da swap chain (cerca de 16 MiB), contados nos dois processos. **[Hipótese]** É a mesma memória.

### Hipóteses

| # | Situação | O que foi visto |
|---|---|---|
| H1 | **Provavelmente refutada** | R em H.264 chegou a 163 MiB no host, tocando no vertical. **[Hipótese]** No ultrawide, a swap chain maior soma cerca de 5 MiB, perto de 168 MiB, acima dos 150 |
| H2 | **Confirmada em parte** | Bitrate e FPS **não mudam nada** (10 a 40 Mbps, 30 a 60 fps). O codec muda muito: HEVC usa **metade** do H.264. Por resolução, cerca de +25 MiB de 2,07 para 2,76 MP |
| H3 | **Confirmada** | Pausado, o host continua com a mesma VRAM. Só encerrar libera, e o adaptador volta à linha de base |
| H4 | **Não testada** | Exige vídeo nos 3 monitores |
| H5 | **Confirmada** | CPU de 0,1% a 0,3% do total de 16 núcleos lógicos, no pior percentil 95 0,8%. A decodificação é por hardware: 7,5% a 8,5% no `Video Codec Engine` |

A estimativa inicial de 40 a 84 MiB só valeu para HEVC. O H.264 custou quase o dobro, por uma razão não investigada.

### Critérios até agora

| Critério | Situação |
|---|---|
| Viabilidade | A1 e A3 passaram; **A2 falhou na primeira tentativa** e foi corrigido, mas ainda precisa ser refeito |
| VRAM (R ≤ 150 MiB) | **Provavelmente reprovado em H.264**, por cerca de 10%. Falta medir no ultrawide. O limite foi fixado antes de medir e não será ajustado depois |
| CPU (R ≤ 5%) | **Aprovado** com folga, nesta medição curta |
| Estabilidade (1 hora, reiniciar o Explorer) | **Não testada** |

Se isso se confirmar, a decisão pelo critério é **manter experimental, atrás de um aviso**. O HEVC ficaria abaixo do limite
também no ultrawide **[Hipótese]** (cenário C1), mas o critério foi definido sobre o cenário R, em H.264.

### Como refazer

```bash
dotnet build src/WallpaperEditor.AnimatedHost -c Release
```

```bash
pwsh scripts/make-test-videos.ps1 -OutDir C:/temp/videos
```

```bash
pwsh scripts/measure-animated.ps1 -Video C:/temp/videos/R.mp4 -OutDir C:/temp/results -Monitor "<device path>"
```

Sem parâmetros de duração, o script segue o protocolo inteiro, com cerca de 20 minutos por cenário.

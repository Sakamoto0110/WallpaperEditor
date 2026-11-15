# 04 — Integração com o Windows

As marcas **[Medido]**, **[Doc]**, **[Terceiro]** e **[Hipótese]** estão definidas no
[README](../README.md#como-ler-o-grau-de-certeza).

## Máquina de referência

**[Medido em 2026-11-06]**, com uma sondagem somente leitura: nenhum wallpaper foi alterado.

| Item | Valor |
|---|---|
| Sistema | Windows 11, build 26300.9711, DisplayVersion 26H2 |
| GPU | AMD Radeon RX 7600, 8 GiB (`HardwareInformation.qwMemorySize`, no registro) |
| .NET SDK | 10.0.403 |
| Extensões de vídeo | HEVC e AV1 instaladas |
| Posição global do wallpaper | `4` (`DWPOS_FILL`) |
| Estado do slideshow | `1` (`DSS_ENABLED`, sem slideshow configurado) |

Duas armadilhas de leitura nessas consultas, para quem repetir:

- O registro `ProductName` diz "Windows 10 Pro" nesta máquina. Identifique a versão pela build.
- O WMI (`Win32_VideoController.AdapterRAM`) informa 4 GB, valor limitado por um campo de 32
  bits. Não use esse campo para VRAM; use DXGI ou os contadores de desempenho.

### Monitores

`GetMonitorDevicePathCount` retorna **5** para **3** monitores físicos:

| # | ID (device path) | Retângulo | Tamanho | É |
|---|---|---|---|---|
| 0 | `\\?\DISPLAY#VIE2900#7&2f1c3d63&1&UID260#{e6f07b5f-…}` | (0, 0) a (2560, 1080) | 2560×1080 | Ultrawide central, primário |
| 1 | `\\?\DISPLAY#STD2025#7&2f1c3d63&1&UID256#{e6f07b5f-…}` | (2560, −452) a (3640, 1468) | 1080×1920 | Vertical à direita |
| 2 | `\\?\DISPLAY#SAM093E#7&2f1c3d63&1&UID268#{e6f07b5f-…}` | (−1920, 0) a (0, 1080) | 1920×1080 | Full HD à esquerda |
| 3 | `legacy_monitor_001` | erro | — | **Fantasma** |
| 4 | `\\?\DISPLAY#STD2025#1&8713bca&0&UID0#{e6f07b5f-…}` | erro | — | **Fantasma** |

Nomes amigáveis, nessa ordem: `NEXUS S29`, `HDMI`, `T24B350`. O monitor vertical se apresenta só como
`HDMI`, então a interface precisa mostrar também resolução e posição.

Observações que viram requisitos:

- **Monitores fantasmas existem.** A contagem inclui monitores desconectados que têm imagem atribuída
  **[Doc]**. Até `GetWallpaper` responde normalmente para eles; só `GetMonitorRECT` denuncia. A lista do
  app filtra por esse retângulo.
- **O índice não identifica um monitor.** Ele não segue a ordem espacial (o índice 2 está à
  esquerda). Salve sempre o device path.
- **O modelo não identifica um monitor.** `STD2025` aparece duas vezes: em um monitor real e em
  um fantasma.
- **Coordenadas negativas são normais** aqui, nos dois eixos. Nenhum cálculo pode supor origem
  em (0, 0).
- **A lista pode trazer uma entrada sem caminho.** **[Medido em 2026-11-07]** `GetMonitorDevicePathAt` devolveu um
  caminho vazio em duas execuções seguidas, sem motivo identificado, e em outras devolveu só os 5 acima. Uma entrada
  assim é ignorada, pois não identifica monitor nenhum.
- **Um monitor pode sumir da lista.** **[Medido em 2026-11-06]** O ultrawide deixou de aparecer como ativo por um
  tempo, e o vertical passou a ser o primário, em (0, 0). Voltou sem nenhuma ação do app, que não altera a configuração
  de monitores; a causa não foi identificada. Por isso nenhuma posição fica guardada.

Essas telas são um caso de teste ideal: três proporções, uma rotação e origem negativa.

## Wallpaper estático: `IDesktopWallpaper`

Interface COM oficial, disponível desde o Windows 8. Cobre tudo o que o modo estático precisa, de
forma documentada. `SystemParametersInfo(SPI_SETDESKWALLPAPER)` **não serve**, porque não tem
parâmetro de monitor.

- CLSID `C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD`
- IID `B92B56A9-8B55-4E14-9A89-0199BBB6F93B`

| Método | Uso | Observação |
|---|---|---|
| `GetMonitorDevicePathCount` / `GetMonitorDevicePathAt` | Enumerar monitores | Inclui fantasmas **[Medido]** |
| `GetMonitorRECT` | Tamanho e posição | Retorna `S_FALSE` para um monitor não conectado **[Doc]**, mas os fantasmas acima retornaram um erro **[Medido]**. Trate os dois casos |
| `SetWallpaper(monitor, caminho)` | Aplicar | Monitor `NULL` afeta **todos** **[Doc]**; o tipo `MonitorId` torna isso impossível. **É assíncrono:** retorna em 3 ms, e o shell termina cerca de 450 ms depois **[Medido]** |
| `GetWallpaper(monitor)` | Ler o atual | Usado para guardar o original |
| `GetPosition` / `SetPosition` | Modo de ajuste | **Global**: não recebe monitor **[Doc]**. Ver abaixo |
| `GetStatus` | Detectar slideshow | Flags `DSS_ENABLED=1`, `DSS_SLIDESHOW=2`, `DSS_DISABLED_BY_REMOTE_SESSION=4` **[Doc, header do SDK]** |

`S_FALSE` é um código de *sucesso*, portanto o wrapper não pode testar apenas `SUCCEEDED`.

### A posição é global

Este é o principal limite da API. Se o app usasse `SetPosition`, ajustar um monitor mudaria os
outros, o que contraria o objetivo central do app.

A solução é nunca depender da posição: o app gera uma imagem com a **resolução exata** do monitor.
Nessa condição, centralizar, preencher, ajustar e esticar produzem o mesmo resultado, seja qual
for a configuração global. O único valor que quebra isso é `DWPOS_SPAN`, que o app detecta
antes de publicar. Ver [ADR 0003](adr/0003-pre-render-na-resolucao-nativa.md).

### Compressão

**[Terceiro]** O Windows guarda uma cópia recomprimida de wallpapers JPEG, com qualidade reduzida
em torno de 85%, controlada pelo valor `JPEGImportQuality` no registro.

**[Medido]** O PNG passa intacto. A cópia do Windows tem o mesmo tamanho em bytes que o original, e
**0 de 2.073.600 pixels** diferem. Por isso o app sempre publica PNG, sem alterar o registro.

### Mesmo caminho, imagem nova

**[Medido]** A suspeita de cache preso ao caminho **não se confirmou**: reescrever o arquivo e chamar
`SetWallpaper` com o mesmo caminho gera uma entrada nova no cache, com os pixels da imagem nova, e a imagem
nova aparece na tela (teste T4).

O app continua usando um nome novo a cada publicação, mas por outro motivo: não sobrescrever um arquivo
que o shell pode estar lendo, já que a chamada é assíncrona.

### O cache do Windows

**[Medido]** Em `%APPDATA%\Microsoft\Windows\Themes`:

| Arquivo | Conteúdo |
|---|---|
| `Transcoded_001` | Cópia da imagem do monitor. O número não acompanha o índice da enumeração |
| `TranscodedWallpaper` | Reescrito a cada troca; é a última etapa visível do trabalho do shell |
| `TranscodedWallpaperCache\TranscodedWallpaper_<hash>` e `.meta` | Cópias recentes. O `.meta` guarda o caminho de origem |

O cache se mantém em **4 entradas** nos testes, mesmo depois de 20 trocas, portanto não cresce sem
limite.

Como o Windows mantém a própria cópia, o arquivo original talvez não seja mais necessário depois da
troca, mas isso não foi testado depois de reiniciar o Explorer. O app assume o pior caso e mantém o
arquivo publicado no disco enquanto ele estiver atribuído.

## Custo de trocar o wallpaper

**[Medido em 2026-11-06]** Com `scripts/measure-swap.ps1`: 20 trocas no monitor vertical (1080×1920), com
PNG de 2,7 MiB e intervalo de 2,5 s. O PC estava em uso normal, então valores pequenos têm ruído.

| Custo | Resultado | Duração |
|---|---|---|
| Chamada `SetWallpaper` | 3 a 4 ms | — |
| **Até o shell terminar de gravar** | **Mediana de 448 ms**, máximo de 496 ms | — |
| **CPU do `explorer`** | **Cerca de 480 ms por troca**, ou seja, um núcleo ocupado durante todo esse tempo | Só durante a troca |
| CPU do `explorer` em repouso | 0,01% | Volta em seguida |
| CPU do `dwm` | Praticamente igual, 1,4% a 1,9% em média | — |
| Memória do `explorer` | De 640 para cerca de 780 MiB | Temporário: volta a 641 MiB |
| **VRAM do `explorer`** | De 120 para cerca de 150 MiB (+28 MiB) | **Temporário:** plano nas 20 trocas, e de volta a 122 MiB em menos de 45 s. **Sem vazamento** |
| Disco do Windows | Cerca de 5 vezes o PNG (14 MiB neste teste) | Limitado pelo cache de 4 entradas |

Somando os 0,5 s medidos para renderizar, **do clique ao fim do trabalho do shell são cerca de 0,9 s**.

**Conclusão:** trocar é uma operação pontual, e **não existe custo contínuo**. Depois da troca, a CPU volta
a zero. Marcar uma tarefa algumas vezes por dia custa menos de um segundo de CPU a cada vez.

**Um monitor com imagem própria não custa VRAM a mais:** o `explorer` estava em 121,6 MiB com a imagem de teste
no monitor vertical, e em 122,0 MiB com a mesma imagem nos três monitores.

**A VRAM total da GPU não serve para medir isso com o PC em uso.** Entre as medições, ela foi de 917 para mais de
1.400 MiB sem nenhuma troca, por causa de outros programas. Por isso, os números acima são os do `explorer`, e o
acréscimo no total da GPU não foi atribuído à troca.

## Custo de abrir o app

**[Medido em 2026-11-06]** Com `scripts/measure-startup.ps1`: build Release, 8 aberturas de cada variação, mediana
das 7 últimas, sem aplicar nada. Nenhuma foi feita logo depois de reiniciar o PC, então uma abertura realmente fria
deve demorar mais.

| Variação | Janela utilizável | Memória aberta (working set / privada) | No disco |
|---|---|---|---|
| Tema Fluent, sem ReadyToRun | 682 ms | 194 / 158 MiB | 1,2 MiB |
| Tema Fluent, com ReadyToRun | 651 ms | 187 / 150 MiB | 1,5 MiB |
| Tema clássico, com ReadyToRun | 576 ms | 194 / 159 MiB | 1,5 MiB |

Em todas, **nenhum processo fica depois de fechar**. Os valores de disco excluem o .NET Desktop Runtime 10, que
precisa estar instalado.

**Decisões:** tema Fluent, porque cerca de 75 ms é um preço baixo por uma interface que segue o tema do sistema; e
**sem ReadyToRun**, pois 31 ms estão dentro do ruído.

A memória, perto de 190 MiB com a janela aberta, é o número menos "leve" do projeto, e sobe para cerca de 266 MiB no
editor, com um projeto de teste aberto e várias publicações em build Debug. Ela só existe enquanto a janela está
aberta, e o uso previsto é abrir, alterar e fechar. Se virar problema, as imagens decodificadas são o primeiro lugar a
investigar. A causa ainda não foi medida.

**Publicar**, medido pela ferramenta de diagnóstico no monitor vertical, com uma imagem de 6000×3375:

| Etapa | Tempo |
|---|---|
| Renderizar, gravar e chamar `SetWallpaper` | 346 a 526 ms |
| Shell terminar o trabalho ([medido antes](#custo-de-trocar-o-wallpaper)) | Cerca de 450 ms |

## Clicar no próprio wallpaper

Marcar uma tarefa clicando no desktop, sem abrir o app, **não é possível sem um processo residente**,
porque o wallpaper é só uma imagem e ninguém recebe o clique. **[Hipótese]** Custo estimado, sem protótipo:

| Item | Custo |
|---|---|
| Processo rodando o tempo todo | Quebra o princípio de zero processos com o app fechado |
| Memória | Cerca de 3 MiB em C++ nativo, ou de 20 a 40 MiB em C#. VRAM: nenhuma |
| CPU | Baixa, mas o processo acorda a cada evento de mouse do sistema inteiro |
| Captura do clique | Raw Input para observar, ou um hook global, que atrasa todo o mouse se o processo travar |
| Distinguir o alvo | Saber se o clique caiu no desktop e fora de um ícone, consultando a janela do Explorer em outro processo |
| Fragilidade | Depende da mesma estrutura não documentada do desktop do modo animado |
| Resposta ao usuário | Os mesmos 0,9 s, sem indicação visual antes do clique |

A troca em si custaria igual; o que se paga é **ficar esperando o clique**. Por isso, o caminho pelo app é o
padrão: um checkbox e a troca, com custo contínuo zero.

## Nome amigável do monitor

`IDesktopWallpaper` não informa nomes. O caminho é `QueryDisplayConfig` +
`DisplayConfigGetDeviceInfo` com `DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME`, que devolve
`monitorFriendlyDeviceName` e `monitorDevicePath`.

**[Medido]** `monitorDevicePath` é igual aos IDs de `IDesktopWallpaper` nos 3 monitores ativos, e por isso
serve de chave para juntar os dois (teste T2). Os fantasmas não aparecem em `QueryDisplayConfig`.

## Modo animado: a janela atrás dos ícones

**Não existe API oficial para isso.** Todo o modo animado depende de detalhes internos do
Windows, que já mudaram uma vez e podem mudar de novo. Esse é mais um motivo para mantê-lo
experimental e isolado.

### As duas topologias

**Clássica** (Windows 10 e Windows 11 até 23H2) **[Terceiro]**: enviar a mensagem não documentada
`0x052C` ao `Progman` faz o shell criar um `WorkerW` de nível superior, atrás dos ícones. A janela
do app vira filha dele.

**Elevada** (builds recentes do Windows 11; relatada como uma atualização que chegou ao longo do
24H2 e do 25H2) **[Terceiro]**: o `Progman` passa a ter `WS_EX_NOREDIRECTIONBITMAP`, e os ícones
(`SHELLDLL_DefView`) viram um filho em camadas do próprio `Progman`. Como a build não define a
topologia com certeza, o host precisa **detectar em tempo de execução**, e não comparar versões.

**[Medido]** Esta máquina está na topologia elevada:

```text
Progman           exstyle=0x200080  rect=(-1920,-452,3640,1468)   ← bit 0x200000 = NOREDIRECTIONBITMAP
└─ SHELLDLL_DefView  exstyle=0x80000                              ← bit 0x80000 = WS_EX_LAYERED
   └─ SysListView32
```

**[Medido]** Até alguém enviar `0x052C`, **não há nenhum `WorkerW` filho do `Progman`**. Os `WorkerW` existentes
são de nível superior, invisíveis, e quase todos com 136×39.

### Como anexar na topologia elevada

**[Medido em 2026-11-07]** Procedimento que funcionou nesta build (26300), com verificação por amostragem de pixels:

1. Detectar: `GetWindowLongPtr(progman, GWL_EXSTYLE) & WS_EX_NOREDIRECTIONBITMAP`.
2. **Enviar `0x052C` ao `Progman`**, se ele ainda não tiver um `WorkerW` filho. O wallpaper passa para esse `WorkerW`.
3. Mandar o `WorkerW` para `HWND_BOTTOM`.
4. Trocar o estilo da janela de `WS_POPUP` para `WS_CHILD`, sem combinar os dois, e acrescentar `WS_DISABLED`.
5. `SetParent` no próprio `Progman`.
6. `SetWindowPos` para ficar logo abaixo de `SHELLDLL_DefView`.

| Variação | Resultado (15 pontos do monitor vertical) |
|---|---|
| Abaixo dos ícones, **sem** `0x052C` | Invisível: os pixels não mudam. A janela existe, visível e no retângulo certo, mas a camada dos ícones a tapa |
| Acima dos ícones, sem `0x052C` | 15 de 15 na cor da janela. Prova que o caminho até a tela funciona |
| Abaixo dos ícones, **com** `0x052C` | 15 de 15 na cor da janela. Confirmado na tela pelo usuário: ícones por cima, clique e menu de contexto funcionando |

A descrição do projeto FeatherWall **[Terceiro]** para esta topologia não cita o `0x052C` e fala em um `WorkerW` do shell
como se ele já existisse. Aqui, essa diferença decide se a janela aparece ou não.

**O `WorkerW` fica.** **[Medido]** Depois que o host fecha, ele continua existindo. O wallpaper segue normal, com alguns
tons até 1 nível diferentes (`F6ECC8` passa a `F6ECC9`), invisíveis a olho nu. **[Medido]** Reiniciar o Explorer remove o
`WorkerW`, e os tons voltam exatamente aos valores originais.

**[Medido]** Com a janela anexada, nesta topologia, **DirectComposition com swap chain flip-model** chega à tela.
GDI, swap chains blt-model e `UpdateLayeredWindow` não aparecem, segundo o mesmo projeto **[Terceiro, não testado]**.
Ver [ADR 0006](adr/0006-reproducao-de-video.md).

**Como verificar sem depender de alguém olhando:** `GetPixel` na tela enxerga esse conteúdo, mas só se nenhuma janela
cobrir o ponto. Uma leitura igual antes e depois pode significar apenas que há uma janela por cima.

### Posicionar em um monitor

Como a janela é filha do `Progman`, sua posição é relativa à origem dele, e não à da tela. Nesta
máquina, a origem é (−1920, −452):

| Monitor | Retângulo na tela | Posição dentro do Progman |
|---|---|---|
| Full HD | (−1920, 0) | **(0, 452)** |
| Ultrawide | (0, 0) | **(1920, 452)** |
| Vertical | (2560, −452) | **(4480, 0)** |

A conversão é `MapWindowPoints`. O código não deve supor nenhum desses valores; a tabela serve como
resultado esperado em teste.

### O que pode quebrar a janela

| Evento | Comportamento | Resposta |
|---|---|---|
| Explorer reinicia | **[Medido]** `Progman` e `SHELLDLL_DefView` são recriados, e o `WorkerW` some. `IDesktopWallpaper` falha com `REGDB_E_CLASSNOTREG` enquanto o Explorer está fora, porque é ele quem serve essa interface | Tratar a falha como "ainda não", e reanexar por `TaskbarCreated` e pelo `WM_DESTROY` da própria janela (A2) |
| `SPI_SETDESKWALLPAPER` | Destrói o `WorkerW` na topologia elevada **[Terceiro]** | O host evita esta chamada durante a reprodução |
| `IDesktopWallpaper::SetWallpaper` com vídeo rodando | **[Medido]** Recria o `WorkerW` logo abaixo dos ícones, por cima do vídeo. O processo e a janela continuam vivos | O host confere a ordem uma vez por segundo e se recoloca. Vídeo coberto por cerca de 1 s a cada troca (teste A3) |
| Bloqueio da sessão | Handles podem mudar **[Terceiro]** | Revalidar ao desbloquear |
| Mudança de monitores ou resolução | Retângulos mudam | Ouvir `WM_DISPLAYCHANGE` e reposicionar |

## Como chamar essas APIs em C#

Tudo o que o app usa fica em `WallpaperEditor.Platform.Windows`. A anexação ao desktop fica no próprio `AnimatedHost`,
para sair junto se o experimento for reprovado:

- **[CsWin32](https://github.com/microsoft/CsWin32)** gera as declarações a partir de uma lista
  (`NativeMethods.txt`). Evita assinaturas escritas à mão, uma fonte comum de bugs sutis.
- Os métodos gerados retornam `HRESULT` onde for preciso diferenciar `S_FALSE` de `S_OK`
  (configurado em `NativeMethods.json`).
- O restante do código vê apenas interfaces do Core (`IMonitorService`, `IWallpaperService`).
- Evitar target framework com versão do Windows fora do `AnimatedHost`: ele traz a projeção WinRT inteira
  para o app, o que contraria a leveza. O `Platform.Windows` suprime o CA1416 pelo motivo explicado no `.csproj`.

## Fontes

- [IDesktopWallpaper](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-idesktopwallpaper)
- [IDesktopWallpaper::SetWallpaper](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-setwallpaper)
- [IDesktopWallpaper::SetPosition](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-setposition)
- [IDesktopWallpaper::GetMonitorRECT](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-getmonitorrect)
- [DESKTOP_WALLPAPER_POSITION](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-desktop_wallpaper_position)
- [FeatherWall — architecture.md](https://raw.githubusercontent.com/RitvikDayal/featherwall/main/docs/architecture.md) (terceiro; topologia elevada)
- Microsoft Q&A: [sobre o Program Manager](https://learn.microsoft.com/en-us/answers/questions/1386630/doubts-about-the-window-of-program-manager) e [sobre as camadas WorkerW](https://learn.microsoft.com/en-gb/answers/questions/1444594/how-to-make-windows-layer-divided-into-workerw1-an) (discussões da comunidade; há resposta afirmando que o `WorkerW` criado por `0x052C` não deve ser manipulado diretamente)
- [Windows Central — compressão de wallpapers](https://windowscentral.com/how-disable-image-compression-desktop-wallpapers-windows-10) e [TekRevue — PNG e JPEG](https://www.tekrevue.com/tip/windows-wallpaper-compression-png-jpeg/) (terceiros)

# 02 — Arquitetura

## A ideia central

O modelo da solução parte de uma observação: **o Windows mantém o wallpaper estático sozinho.** Depois da
chamada à API, o app pode fechar. Só o vídeo exige um processo vivo.

Por isso:

- Static e Dynamic terminam da mesma forma: uma imagem plana, entregue ao Windows. Nenhum dos dois deixa
  processo rodando.
- Animated é outro mundo, em outro processo, que pode ser removido sem afetar os demais.

## Projetos

```mermaid
graph TD
    App["WallpaperEditor.App<br/>WPF, MVVM"]
    Core["WallpaperEditor.Core<br/>documento, projeto, histórico"]
    Rendering["WallpaperEditor.Rendering<br/>documento para bitmap, WPF"]
    Platform["WallpaperEditor.Platform.Windows<br/>COM, P/Invoke"]
    Host["WallpaperEditor.AnimatedHost<br/>processo experimental"]

    App --> Core
    App --> Rendering
    App --> Platform
    Rendering --> Core
    Platform --> Core
    Host --> Platform
    App -. "inicia e encerra o processo" .-> Host
```

| Projeto | Responsabilidade | Não pode |
|---|---|---|
| **Core** | Tipos (`Monitor`, `Project`, `Revision`, `Document`, itens), regras de histórico, leitura e escrita da pasta de projeto, interfaces (`IWallpaperService`, `IMonitorService`, `IWallpaperRenderer`) | Referenciar WPF, Win32 ou qualquer API de imagem |
| **Rendering** | Transformar um documento em bitmap e PNG; ajustar uma imagem ao monitor | Saber o que é um monitor do Windows; recebe apenas tamanhos em pixels |
| **Platform.Windows** | Falar com o Windows: enumerar monitores, aplicar wallpaper, ler estado, encontrar a janela do desktop | Conter regra de negócio |
| **App** | Janelas, editor, view models, composição das dependências | Chamar Win32 diretamente |
| **AnimatedHost** | Reproduzir um vídeo atrás dos ícones em um monitor | Depender do App, do Rendering ou de WPF |

Fora de `src/`, `tools/WallpaperEditor.Diagnostics` valida os comportamentos do Windows e mede custos, para que
tudo possa ser repetido em uma build futura. Ela usa o mesmo `Platform.Windows`, e só escreve no monitor vertical.

A direção das dependências é o que torna o projeto testável: o `Core` roda em testes sem nada do Windows, e o
`Rendering` não precisa de janela nem de monitor. A justificativa da divisão está no
[ADR 0002](adr/0002-divisao-do-projeto.md).

**Target frameworks:** `net10.0` para Core; `net10.0-windows` para App, Rendering e Platform.Windows;
`net10.0-windows10.0.19041.0` para AnimatedHost, que usa uma API WinRT.

**Publicação do app:** framework-dependent, sem ReadyToRun, que [não trouxe ganho
mensurável](04-integracao-windows.md#custo-de-abrir-o-app). Exige o .NET Desktop Runtime 10 instalado.

## O fluxo de dados

Tudo o que o app faz cabe neste caminho. Um wallpaper estático é apenas um documento sem itens e sem histórico:

```mermaid
graph LR
    F["Arquivos-fonte"] --> D["Documento<br/>fundo + itens"]
    D --> R["Renderizar"]
    R --> P["PNG plano"]
    P --> W["SetWallpaper"]
    H["Revisões"] <--> D
```

## Fluxo: publicar

```mermaid
sequenceDiagram
    actor U as Usuário
    participant A as App
    participant R as Rendering
    participant P as Platform.Windows
    participant W as Windows

    U->>A: Publicar neste monitor
    A->>P: Monitor ainda está ativo? Qual o tamanho?
    P-->>A: 1080x1920
    A->>R: Renderizar documento em 1080x1920
    R-->>A: PNG em pasta temporária
    A->>A: Mover para published/ com nome novo
    A->>A: Salvar o wallpaper original, se for a primeira vez
    A->>P: SetWallpaper(monitor, arquivo)
    P->>W: IDesktopWallpaper::SetWallpaper
    W-->>P: OK
    A->>A: Salvar atribuição e limpar arquivos antigos
```

A ordem importa, e é coberta por testes em `WallpaperPublisher`:

- **O original é salvo antes da troca.** Se o app cair logo depois dela, o original não se perde.
- **A atribuição só é gravada depois do sucesso.** Uma falha em qualquer etapa deixa o monitor e o estado como
  estavam, e remove o arquivo novo.
- **Ficam o arquivo atual e o anterior.** Como a chamada é assíncrona, o anterior pode ainda estar em uso durante
  trocas rápidas, como ao marcar várias tarefas. Isso custa um PNG a mais em disco.
- **Um wallpaper trocado fora do app vira o novo original,** pois é a escolha mais recente do usuário.

**Esse OK significa "agendado".** `SetWallpaper` retorna em 3 ms, e o shell trabalha por mais cerca de 450 ms
([custo medido](04-integracao-windows.md#custo-de-trocar-o-wallpaper)). `GetWallpaper` já informa o caminho novo
antes disso, então não serve para saber se a troca terminou.

As razões para pré-renderizar e usar nome novo a cada publicação estão no
[ADR 0003](adr/0003-pre-render-na-resolucao-nativa.md).

## Fluxo: editar um wallpaper dinâmico

```mermaid
stateDiagram-v2
    [*] --> Aberto: abrir projeto
    Aberto --> Alterando: adicionar, mover ou editar item
    Alterando --> Aberto: concluir a ação cria revisão
    Alterando --> Aberto: cancelar
    Aberto --> Aberto: reverter ou restaurar item cria revisão
    Aberto --> Aberto: publicar
    Aberto --> [*]: fechar
```

- **Salvar é automático.** Toda ação concluída vira uma revisão, e não existe "salvar" nem alteração perdida.
- **Publicar é explícito, exceto ao marcar uma tarefa,** que já faz a troca (Q3 da
  [visão do produto](01-visao-do-produto.md#perguntas-em-aberto)). Nos outros casos, o head pode estar à frente
  do que está no monitor, e a tela mostra essa diferença de forma visível.
- **O histórico mostra as mudanças em texto,** como "Adicionou a checklist Hoje" ou "Marcou a tarefa", com uma
  miniatura da revisão selecionada, gerada na hora.
- **Marcações rápidas se juntam.** `CoalescingRunner` faz uma publicação de cada vez; pedidos feitos durante uma
  delas viram uma só no final, que sempre inclui o último clique.

## Fluxo: modo animado

```mermaid
sequenceDiagram
    participant A as App
    participant H as AnimatedHost
    participant W as Shell do Windows

    A->>H: iniciar --monitor ID --video ARQUIVO
    H->>W: Detectar topologia e anexar janela
    H->>H: Reproduzir em loop
    Note over A: O App pode fechar
    W-->>H: Explorer reiniciou
    H->>W: Reanexar
    A->>H: Encerrar (evento nomeado)
    H->>W: Soltar janela e redesenhar o desktop
```

Um processo por monitor animado. A troca de mensagens é a mínima possível: argumentos de linha de comando para
iniciar e um evento nomeado para encerrar.

## Estado do app

Duas categorias, separadas de propósito:

| Onde | O que | Por quê |
|---|---|---|
| Pasta do projeto (local escolhido pelo usuário) | Assets, revisões | Portátil: pode ser movida ou copiada para outra máquina |
| `%LOCALAPPDATA%\WallpaperEditor\` | Atribuições, arquivos publicados, logs | Específico desta máquina e destes monitores |

```text
%LOCALAPPDATA%\WallpaperEditor\
├─ state.json          atribuições e wallpapers originais
├─ published\
│  └─ <hash-do-monitor>\
│     └─ <id-único>.png
└─ logs\
```

Os arquivos ficam em `published\`, e não na pasta do projeto, para que mover ou apagar um projeto não quebre o
que está na tela.

Na primeira vez que o app toca em um monitor, o `state.json` guarda o wallpaper que estava lá. Isso permite o
comando "restaurar original".

Ao abrir, o app volta direto ao último projeto usado, já que a abertura é o caminho normal para fazer uma
pequena alteração.

## Editor (WPF)

- **Padrão:** MVVM com `CommunityToolkit.Mvvm`. A `NekoLib.Navigation.Wpf` foi avaliada e descartada: é voltada a
  navegação em camadas para kiosk e PDV, e seria peso sem uso em um app de uma ou duas telas.
- **Tema:** Fluent (`ThemeMode="System"`), que segue o claro ou escuro do Windows, sem dependência nova.
- **Um só código de desenho.** `DocumentPainter` desenha tanto na tela quanto no PNG final, então o que aparece no
  editor é o que será publicado.
- **Arrastar não recompõe nada.** Em `EditorSurface`, cada item é um elemento próprio. Um arrasto só muda a posição
  dele, e o documento só muda ao soltar, gerando uma única revisão.
- **Manipulação:** as alças de redimensionar e girar ficam em uma camada à parte, e não fazem parte da imagem. A
  matemática de itens girados fica em `ItemGeometry`, no Core, onde pode ser testada.
- **Lista de itens:** alcança um item escondido atrás de outro, e funciona sem mouse.
- **Checkboxes por binding, não por comando,** porque o UI Automation, usado por leitores de tela, não dispara o
  comando de um checkbox.
- **DPI:** o manifesto declara `PerMonitorV2`. Sem isso, os retângulos dos monitores vêm virtualizados, e as
  imagens seriam geradas no tamanho errado.
- **Renderização final** roda em uma thread STA dedicada, fora da thread de UI.

## Erros

| Situação | Comportamento |
|---|---|
| Monitor desconectado | Atribuição fica guardada, sem aparecer na lista, e nunca é redirecionada a outro monitor |
| Resolução ou orientação do monitor mudou | Aviso para aplicar de novo. Ignorado se o wallpaper atual não for do app |
| Imagem ilegível ou removida | Mensagem na tela, e o wallpaper não muda |
| `state.json` danificado ou de versão mais nova | Arquivo guardado à parte (`.unreadable`), e o app abre com estado vazio |
| Modo span ativo no Windows | Publicar é bloqueado, com explicação, e o usuário decide mudar (a posição é global e afeta os outros monitores) |
| Slideshow do Windows ativo | Aviso de que o slideshow vai sobrescrever o wallpaper |
| Pasta de projeto em uso por outra instância | Abrir como somente leitura, por meio do `.lock` |
| Revisão corrompida | Revisão marcada como inválida; as demais continuam utilizáveis (cada revisão é completa) |
| Asset ausente ou corrompido | O item aparece com um marcador de erro no editor, e publicar pede confirmação |
| Fonte do sistema ausente | Aviso, com uma fonte alternativa |
| `AnimatedHost` termina inesperadamente | Esperado: o desktop volta ao wallpaper estático do monitor, a confirmar na Fase 5. O App mostra o estado como parado |

## Testes

| Camada | Abordagem |
|---|---|
| Core | xUnit, com dublês do Windows e do renderizador: monitores, ajuste, publicação, estado, projeto, histórico, recuperação de escrita interrompida, geometria de itens girados |
| Rendering | xUnit, com imagens geradas no teste: dimensões exatas, os 4 ajustes, EXIF, pixel a pixel, ordem e rotação de itens, recorte de checklists, erros |
| Platform.Windows | Sem teste automatizado; os testes alteram o desktop real. Roteiro manual em [06 — Roadmap](06-roadmap.md) |
| App | Sem testes automatizados: a lógica fica no Core, e a tela é conferida à mão ou por UI Automation. Permite `--data` e `--project`, para testar sem tocar no estado real |

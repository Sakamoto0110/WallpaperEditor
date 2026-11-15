# WallpaperEditor

Aplicativo Windows para definir e **editar** o wallpaper de **um monitor específico**, sem
afetar os outros.

**Extremamente leve, e não um Wallpaper Engine:** o Windows recebe uma imagem estática comum, e com
o app fechado nada fica rodando, a não ser o pequeno **companion** enquanto houver um wallpaper dinâmico na tela, para
usá-lo sem abrir o editor. O wallpaper só muda pelo app.

> **Status: Fase 4 em conferência, Fase 5 em andamento.** Static e Dynamic funcionam: o editor compõe imagens,
> checklists, texto e componentes clicáveis (contador, barra de progresso, alternância), com histórico para reverter, e
> publica em um monitor. Animated existe só como experimento: o `AnimatedHost` roda por linha de comando, fora da
> interface, para medir o custo. O estado de cada fase, e o que falta testar, está em
> [docs/06-roadmap.md](docs/06-roadmap.md).

## Compilar e testar

```bash
dotnet build WallpaperEditor.slnx
```

```bash
dotnet test WallpaperEditor.slnx
```

```bash
dotnet run --project src/WallpaperEditor.App
```

## Tipos de wallpaper

| Tipo | O que é | Precisa de processo rodando? |
|---|---|---|
| **Static** | Uma imagem, ajustada ao monitor escolhido | Não |
| **Dynamic** | Uma composição de camadas (imagens como cheatsheets de uma linguagem, checklists, texto, contadores, barras de progresso e alternâncias), com pasta de projeto, arquivos-fonte e histórico. Os componentes são clicáveis na pré-visualização do app, e o wallpaper só muda em **Atualizar wallpaper**. Fora do editor, o companion abre o wallpaper numa janela onde cada clique o atualiza na hora. A saída é um PNG plano e irreversível; o **registro de alterações** é o que permite reverter e recompor | Só o companion, opcional para o wallpaper continuar na tela |
| **Animated** | Um vídeo `.mp4` em loop. **Experimental:** existe para descobrir quanta VRAM isso consome | Sim |

## Documentação

Leia na ordem se estiver chegando agora:

| Documento | Responde a |
|---|---|
| [01 — Visão do produto](docs/01-visao-do-produto.md) | O que o app faz, o que não faz, vocabulário, perguntas em aberto |
| [02 — Arquitetura](docs/02-arquitetura.md) | Como o código se divide, e como funciona cada fluxo |
| [03 — Formato de projeto](docs/03-formato-de-projeto.md) | O que há na pasta de um wallpaper dinâmico, e como o histórico funciona |
| [04 — Integração com o Windows](docs/04-integracao-windows.md) | APIs usadas, comportamentos medidos nesta máquina, armadilhas |
| [05 — Experimento do modo animado](docs/05-experimento-animado.md) | Hipóteses, protocolo de medição de VRAM, critério de decisão |
| [06 — Roadmap](docs/06-roadmap.md) | Fases, critérios de conclusão, o que validar antes de construir |

### Decisões (ADRs)

| # | Decisão |
|---|---|
| [0001](docs/adr/0001-wpf-dotnet10.md) | WPF sobre .NET 10 |
| [0002](docs/adr/0002-divisao-do-projeto.md) | Dividir em assemblies; um processo separado só para o modo animado |
| [0003](docs/adr/0003-pre-render-na-resolucao-nativa.md) | Toda publicação é pré-renderizada na resolução nativa do monitor |
| [0004](docs/adr/0004-composicao-recriavel.md) | Saída irreversível, projeto recriável por registro de alterações |
| [0005](docs/adr/0005-renderizacao-com-wpf.md) | Renderização com WPF, sem biblioteca de imagem externa |
| [0006](docs/adr/0006-reproducao-de-video.md) | `MediaPlayer` do Windows + DirectComposition no modo animado |
| [0007](docs/adr/0007-companion-residente.md) | Um companion residente enquanto houver um wallpaper dinâmico |

### Como ler o grau de certeza

Os documentos marcam de onde cada afirmação técnica vem, porque boa parte da integração com o
desktop do Windows não é documentada:

| Marca | Significa |
|---|---|
| **[Medido]** | Observado nesta máquina (Windows build 26300), com data |
| **[Doc]** | Documentação oficial da Microsoft ou header do SDK |
| **[Terceiro]** | Afirmado por outro projeto; não confirmado por nós |
| **[Hipótese]** | Raciocínio nosso, ainda sem verificação; tem um teste previsto no roadmap |

## Layout do repositório

Segue a convenção dos outros projetos em `dev/`.

```text
WallpaperEditor/
├─ WallpaperEditor.slnx
├─ global.json
├─ Directory.Build.props
├─ README.md
├─ docs/
├─ scripts/                              medições repetíveis
├─ tools/
│  └─ WallpaperEditor.Diagnostics/       valida comportamentos do Windows e mede custos
├─ src/
│  ├─ WallpaperEditor.Core/              documento, projeto, histórico (sem UI, sem Win32)
│  ├─ WallpaperEditor.Rendering/         documento para bitmap e PNG (WPF, sem janela)
│  ├─ WallpaperEditor.Platform.Windows/  COM e P/Invoke: monitores, aplicar wallpaper
│  ├─ WallpaperEditor.App/               aplicativo WPF
│  └─ WallpaperEditor.AnimatedHost/      processo do modo animado (experimental)
└─ tests/
   ├─ WallpaperEditor.Core.Tests/
   └─ WallpaperEditor.Rendering.Tests/
```

## Pré-requisitos previstos

- Static e Dynamic usam uma API oficial disponível desde o Windows 8. O modo animado exige a
  topologia recente do desktop do Windows 11, a mesma medida nesta máquina (build 26300); veja
  [docs/04-integracao-windows.md](docs/04-integracao-windows.md).
- .NET SDK 10.0.403 (já instalado nesta máquina).


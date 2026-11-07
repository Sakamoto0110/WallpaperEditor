# ADR 0002 — Dividir em assemblies; processo separado só para o modo animado

**Status:** Aceita · **Data:** 2026-11-06

## Contexto

A ideia inicial levantou uma questão: usar P/Invoke direto ou dividir o projeto em dois, um para
cuidar da WinAPI e outro para edição e publicação dos wallpapers.

São na verdade duas perguntas diferentes:

1. **Separar o código?** Isolar a WinAPI em um módulo próprio.
2. **Separar a execução?** Rodar a WinAPI em um programa à parte.

## Decisão

**Sim para a primeira. Só para o modo animado na segunda.**

- A WinAPI fica em um **assembly próprio** (`WallpaperEditor.Platform.Windows`), chamado em
  processo, por P/Invoke e COM, a partir do C#.
- Só existe um **segundo processo** para o vídeo (`WallpaperEditor.AnimatedHost`).

## Por quê

**Um processo separado para a WinAPI não traz benefício nenhum.** As chamadas de wallpaper são
raras, rápidas e não precisam de elevação. O Windows mantém o wallpaper depois que o app fecha.
Um segundo programa só acrescentaria protocolo de comunicação, sincronização de versões e mais
pontos de falha.

**Um assembly separado traz benefícios reais:**

- `Core` e `Rendering` podem ser testados sem alterar o desktop nem depender dos monitores.
- Todo o código inseguro e não documentado fica em um só lugar.
- O modelo de dados não depende do WPF, e o `Platform.Windows` também não.

**O vídeo é diferente, e ali o processo separado se paga:**

| Motivo | Detalhe |
|---|---|
| Ciclo de vida | O vídeo precisa continuar depois que o editor fecha. Manter o editor WPF aberto só para isso custaria memória sem necessidade |
| **Medição** | O objetivo do experimento é medir VRAM, e os contadores do Windows são **por processo**. No mesmo processo do editor, a medição ficaria contaminada |
| Falhas | Um driver de vídeo ou decodificador que falhe não leva o editor junto |
| Remoção | Se o experimento for reprovado, basta apagar um projeto |

## Alternativas

| Opção | Por que não |
|---|---|
| Um único projeto, com P/Invoke espalhado | Impede testes sem desktop e deixa código perigoso disperso |
| Serviço ou processo residente para a WinAPI | Complexidade sem benefício, pelos motivos acima |
| Biblioteca nativa em C++ para a WinAPI | O C# chama essas APIs sem dificuldade. O `AnimatedHost` pode ser reavaliado se o experimento mostrar que o C# limita a medição, mas não há indício disso |

## Consequências

- Cinco projetos em `src/`. Parece muito para o início, mas cada divisão tem um motivo específico
  acima.
- A chamada entre App e host usa o mínimo: linha de comando e um evento nomeado.
- `AnimatedHost` referencia `Platform.Windows` para reutilizar a enumeração de monitores, o que
  evita duplicar essa lógica.

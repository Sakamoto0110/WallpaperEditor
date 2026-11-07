# ADR 0004 — Saída irreversível, projeto recriável por registro de alterações

**Status:** Aceita · **Data:** 2026-11-06

## Contexto

Exemplos de wallpaper dinâmico:

- um wallpaper com parte da referência de uma linguagem, por meio de uma imagem tipo cheatsheet;
- adicionar e remover checklists e listas de tarefas;
- adicionar imagens em cima da imagem atual.

O wallpaper que chega ao Windows é uma imagem estática, e o único jeito de alterá-lo é pelo app.
Por isso é preciso um histórico dos itens adicionados e removidos e dos arquivos-fonte, para refazer
a composição.

> **Correção.** A primeira versão deste ADR entendeu "colocar uma imagem dentro da outra" como fundir
> pixels de forma definitiva no próprio projeto, guardando um PNG por revisão, sem permitir mover nada
> depois. Os exemplos acima mostram que a fusão acontece só **na saída**. Uma checklist não funcionaria
> no modelo antigo: marcar uma tarefa exige redesenhá-la sem o estado anterior por baixo.

## Decisão

Existem duas coisas, com regras opostas:

| | **Saída** (o que o Windows recebe) | **Projeto** (o que o app guarda) |
|---|---|---|
| Forma | Um PNG plano | Fundo + lista de itens + arquivos-fonte |
| Reversível? | **Não.** Nada nele separa os itens | **Sim.** Tudo pode ser recriado |
| Alterar | Impossível: é substituído por outro | Cada alteração cria uma revisão |

**Reverter nunca desfaz o PNG.** Reverter restaura o estado do projeto, e publicar gera um PNG novo a
partir desse estado.

Cada revisão é um **JSON com o estado completo** do documento, mais a descrição da mudança. Os
arquivos-fonte ficam guardados por hash e são imutáveis. O histórico é linear e só cresce: reverter
cria uma revisão nova.

## Por quê

- **Atende aos exemplos.** Marcar uma tarefa altera um campo; remover uma checklist tira um item da lista.
- **Leve.** Uma revisão ocupa poucos KiB, em vez de alguns MiB. Uma lista de tarefas pode gerar milhares
  de revisões sem custo perceptível.
- **Estado completo, não operações.** Abrir uma revisão é ler um arquivo, sem reexecutar nada. Uma
  revisão corrompida não afeta as outras, e o que mudou entre duas revisões pode ser calculado.
- **Restauração por item.** Como os itens têm ID estável, dá para trazer de volta **um item** de uma
  revisão antiga sem reverter o resto.

## Alternativas

| Opção | Por que não |
|---|---|
| PNG por revisão, com pixels fundidos (versão anterior) | Não permite alterar ou remover um item; gasta MiB por revisão |
| Log de operações reexecutadas | Mesmo resultado, mas abrir uma revisão exige repetir tudo desde o início, e um bug em uma operação antiga corrompe todas as seguintes |
| Histórico ramificado, estilo git | Complexidade sem necessidade: linear com reversão não perde nada |
| Sem histórico, só o estado atual | Contraria o requisito |

## Consequências

- **Dentro do projeto, itens funcionam como camadas:** têm ordem e podem ser movidos. A diferença para um
  editor de camadas é o escopo: nenhuma ferramenta de edição de pixels, e nenhuma camada chega ao desktop.
- **Duas renderizações da mesma revisão podem não ser idênticas**, se a versão do WPF ou uma fonte do
  sistema mudar. Isso é aceitável, porque a revisão registra a intenção; o PNG publicado continua em disco
  até ser substituído.
- **Arquivos-fonte nunca são apagados por uso normal**, pois revisões antigas apontam para eles. O espaço
  ocupado por eles é o único custo relevante em disco, e a tela do projeto o mostra.

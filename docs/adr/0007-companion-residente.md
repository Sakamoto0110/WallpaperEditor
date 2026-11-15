# ADR 0007 — Um companion residente enquanto houver um wallpaper dinâmico

**Status:** Aceita; testada pelo usuário, incluindo voltar sozinho depois de reiniciar · **Data:** 2026-11-15

## Contexto

Marcar uma tarefa exigia abrir o editor, achar o projeto e clicar em **Atualizar wallpaper**. Para algo que se faz
várias vezes por dia, é lento demais. O usuário pediu um jeito rápido de interagir com o wallpaper, num processo
próprio, rodando sempre que houver um wallpaper dinâmico na tela.

Isso contraria um princípio da [visão do produto](../01-visao-do-produto.md#extremamente-leve): com o app fechado,
nenhum processo. Clicar no próprio desktop continua fora do escopo
([custo estimado](../04-integracao-windows.md#clicar-no-próprio-wallpaper)): o companion não observa o mouse, só
responde à própria janela.

## Decisão

O mesmo executável, com `--companion`, vira um processo à parte:

- **Ícone na área de notificação.** Um clique abre a janela do wallpaper dinâmico (com vários, o menu pergunta qual).
  O menu também abre o editor e fecha o companion.
- **Janela de interação.** O wallpaper do tamanho de cerca de 72% da tela, sem maximizar, onde só os componentes
  respondem: tarefas, − e + do contador, a barra arrastável e a alternância. Nada pode ser selecionado, movido ou
  redimensionado. **Cada clique atualiza o wallpaper**; cliques rápidos se juntam em uma troca a mais no fim, pelo
  mesmo `CoalescingRunner` do editor.
- **Vive exatamente enquanto algum monitor mostra um projeto:** o editor e a tela de monitores sincronizam depois de
  publicar ou restaurar. O companion inicia se for preciso, recebe `exit` quando não há mais nada, e se registra em
  `HKCU\...\Run` só enquanto for necessário. Se o wallpaper foi trocado fora do app, ele sai sozinho no próximo
  início.
- **Uma instância por pasta de dados:** um mutex e um named pipe com o hash da pasta. Uma execução com `--data` tem o
  seu próprio companion e **nunca** mexe no início com o Windows.
- **Só segura o projeto com a janela aberta.** Fechada, o editor abre o projeto normalmente; aberta, o editor o abre
  somente para leitura, e vice-versa.

## Custo medido

Nesta máquina, com o projeto de teste (`scratchpad`, 2026-11-15):

| Situação | Privada | Em RAM (working set) |
|---|---|---|
| Iniciado sem janela, como no login | 16 MiB | 10 MiB |
| Com a janela aberta | 98 MiB | 140 MiB |
| Depois de fechar a janela | 85 MiB | 25 MiB |

Ao fechar a janela, o companion devolve a memória das imagens decodificadas ao Windows. O que sobra de memória privada
fica reservada pelo WPF, fora da RAM, até o processo reiniciar. CPU parado: nenhum timer; ele só espera no pipe e no
ícone.

## Consequências

- O princípio passa a ser: **sem wallpaper dinâmico na tela, nenhum processo.** Com um, o companion.
- O Windows Forms entra no App só para o ícone da área de notificação, que o WPF não tem.
- Os dois processos gravam `state.json`. A escrita usa um arquivo temporário único, e cada uma substitui o arquivo
  inteiro.

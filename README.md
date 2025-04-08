# Playnite Control Center

O **Playnite Control Center** é um plugin personalizado para o Playnite que adiciona um overlay interativo, inspirado em interfaces de consoles, como o PlayStation 5. Ele foi projetado para melhorar a experiência de uso do Playnite, permitindo que os usuários controlem jogos e acessem funções diretamente de uma sobreposição na tela, sem precisar minimizar ou sair do jogo.

## O que ele faz?
- **Overlay em Tela Cheia**: Exibe uma interface elegante e funcional sobre o jogo em execução, acessível com um atalho (padrão: `Alt+``).
- **Controle Total**: Suporte completo a controles (via SDL2) e teclado, com navegação fluida entre opções.
- **Sons Personalizados**: Inclui efeitos sonoros de navegação (`menu_navigate.wav`) e seleção (`menu_select.wav`), trazendo uma sensação de console.
- **Funcionalidades Práticas**:
  - **Voltar ao Playnite**: Retorna rapidamente à biblioteca do Playnite.
  - **Gerenciar Jogos**: Permite retornar ao jogo ativo ou fechá-lo diretamente do overlay.
  - **Ações Adicionais**: Botões para volume, usuário e opções de energia (em desenvolvimento).
- **Integração com o Playnite**: Usa a API do Playnite para interagir com jogos em execução e a biblioteca.

## Interface
A interface do Playnite Control Center é intuitiva e inspirada em designs modernos de consoles:
- **Barra Principal**: Cinco botões grandes e visualmente distintos:
  - **Início**: Volta ao Playnite.
  - **Jogos**: Abre uma grade para gerenciar o jogo ativo (retornar ou fechar).
  - **Volume**: (Futuro) Controle de áudio.
  - **Usuário**: (Futuro) Perfil ou configurações.
  - **Energia**: (Futuro) Opções de desligamento ou suspensão.
- **Grade de Jogos**: Uma subseção que aparece ao selecionar 'Jogos', com opções para retornar ao jogo ou fechá-lo.
- **Navegação Sonora**: Cada movimento entre botões ou seleções é acompanhado por sons característicos, criando uma experiência imersiva.

### Screenshots

O design é limpo, com foco em usabilidade, e pode ser personalizado ou expandido com mais funcionalidades no futuro.

## Instalação
1. Baixe o arquivo `.pext` na seção [Releases](https://github.com/marcospc20/Playnite-Control-Center/releases).
2. Arraste o arquivo para a janela do Playnite ou clique duas vezes para instalar.
3. Reinicie o Playnite, e o overlay estará disponível durante os jogos.

## Requisitos
- Playnite instalado (Desktop ou Fullscreen mode).
- Arquivos de som (`menu_navigate.wav` e `menu_select.wav`) incluídos no pacote.

## Contribuições
Sinta-se à vontade para sugerir melhorias ou contribuir com o código! Veja os arquivos no repositório e envie pull requests.

## Autor
Desenvolvido por MarcosPC (`marcospc20`).

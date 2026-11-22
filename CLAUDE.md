# CLAUDE.md — Atlas Literário

## 1. Contexto do projeto

O Atlas Literário é uma aplicação web de catalogação de livros e exploração geográfica da literatura.

O objetivo é permitir que usuários organizem suas bibliotecas pessoais e visualizem, através de mapas-múndi interativos, os países que conheceram por meio das obras e dos autores que leram.

O principal diferencial são dois mapas independentes:

1. **Mapa de nacionalidades dos autores:** países associados às nacionalidades ou cidadanias dos autores dos livros concluídos.
2. **Mapa de origem das obras:** países associados à origem literária das obras concluídas.

A aplicação também terá estatísticas, Passaportes Literários, dashboard e compartilhamento dos mapas.

O nome do projeto é Atlas Literário.

## 2. Stack tecnológica

### Backend

- C#
- .NET 10 LTS
- ASP.NET Core Web API
- Entity Framework Core
- PostgreSQL
- JWT
- Swagger / OpenAPI
- Docker

### Frontend

- Vue.js 3
- TypeScript
- Vite
- Composition API
- Pinia
- Vue Router
- Axios
- MapLibre GL JS ou alternativa tecnicamente justificada.

### Infraestrutura

- Backend e frontend desacoplados.
- Comunicação através de API REST.
- PostgreSQL como banco de dados principal.
- Docker para padronização do ambiente.

Não adicionar tecnologias ou dependências sem avaliar sua necessidade.

## 3. Arquitetura do projeto

Utilizar arquitetura organizada, modular e com separação clara de responsabilidades.

### Backend

Seguir princípios de Clean Architecture:

- Domain: entidades e regras fundamentais do domínio.
- Application: casos de uso, serviços, DTOs e interfaces.
- Infrastructure: persistência, integrações externas e implementações técnicas.
- API: controllers, autenticação, configuração e exposição dos endpoints.

Regras:

- Controllers não devem conter regras de negócio.
- Não acoplar o domínio a serviços externos.
- Utilizar injeção de dependência.
- Centralizar tratamento de exceções.
- Utilizar DTOs para comunicação externa.
- Evitar exposição direta das entidades do banco.
- Manter as responsabilidades de cada camada bem definidas.

### Frontend

Organizar por funcionalidades e componentes reutilizáveis.

Separar:

- Pages.
- Components.
- Services.
- Stores.
- Composables.
- Types.
- Router.
- Layouts.

Regras:

- Utilizar Composition API.
- Preferir componentes pequenos e reutilizáveis.
- Centralizar comunicação HTTP.
- Centralizar tratamento de erros da API.
- Utilizar Pinia para estados compartilhados.
- Evitar lógica de negócio complexa dentro dos componentes.
- Utilizar TypeScript de maneira consistente.

## 4. Organização dos agentes

O projeto deverá possuir três responsabilidades:

### Agente principal — Architect / Coordinator

Responsável por:

- Definir arquitetura.
- Estabelecer contratos.
- Coordenar os agentes.
- Resolver conflitos.
- Revisar implementações.
- Validar integração.
- Acompanhar o planejamento.
- Manter a documentação atualizada.

### Backend Developer

Responsável exclusivamente pelo desenvolvimento do backend.

Diretório principal: `/backend`.

### Frontend Developer

Responsável exclusivamente pelo desenvolvimento do frontend.

Diretório principal: `/frontend`.

### Regras de colaboração

1. Backend e frontend devem trabalhar simultaneamente sempre que suas tarefas forem independentes.
2. Os agentes devem respeitar os contratos previamente definidos.
3. O frontend pode utilizar mocks enquanto os endpoints não estiverem disponíveis.
4. O backend deve documentar os contratos utilizando OpenAPI.
5. O OpenAPI será a referência oficial dos contratos HTTP.
6. Alterações incompatíveis nos contratos precisam ser comunicadas ao agente principal.
7. Nenhum agente deve modificar arquivos de responsabilidade exclusiva do outro.
8. Utilizar Git Worktrees ou isolamento equivalente quando necessário para evitar conflitos.
9. Não permitir que os agentes implementem soluções concorrentes para o mesmo problema sem alinhamento prévio.
10. O agente principal deve revisar os resultados antes de considerar uma etapa concluída.

Não afirmar que os agentes estão trabalhando simultaneamente sem verificar que foram efetivamente iniciados.

## 5. Regras fundamentais de negócio

### Biblioteca

Cada usuário possui sua própria biblioteca.

O cadastro bibliográfico de um livro deve ser independente da experiência de leitura do usuário.

Um mesmo livro pode pertencer a vários usuários, mas cada usuário possui:

- Status de leitura.
- Avaliação.
- Resenha.
- Data de início.
- Data de conclusão.

Status possíveis:

- WantToRead.
- Reading.
- Read.
- Abandoned.

### Mapas

Por padrão, somente livros com status Read participam dos mapas.

Livros desejados, em andamento ou abandonados não contam como leituras concluídas.

O usuário pode visualizar o mapa filtrado por outro status (WantToRead, Reading, Abandoned) ou por todos (All). É apenas um filtro de visualização: o padrão continua sendo Read, e Passaportes Literários, estatísticas e compartilhamento seguem considerando somente livros Read.

Os mapas devem ser independentes:

- Author Nationalities.
- Book Origins.

Um autor pode ter múltiplas nacionalidades ou cidadanias.

Uma obra pode ter múltiplos países de origem, quando aplicável.

Não confundir:

- Nacionalidade do autor.
- País de nascimento do autor.
- País de residência do autor.
- País onde a história acontece.
- País de publicação.
- País de origem literária da obra.

Essas informações possuem significados diferentes e não devem ser utilizadas como equivalentes.

### Estatísticas

Distinguir:

- Quantidade de livros por país.
- Quantidade de autores por país.
- Quantidade de países explorados.

Um país deve ser contabilizado apenas uma vez no total de países explorados de cada mapa.

Evitar duplicações quando uma obra possuir vários autores ou vários países associados.

As estatísticas devem ser individualizadas por usuário.

Alterações no status de leitura devem refletir nos mapas e indicadores.

## 6. Integrações bibliográficas

Fontes previstas:

### Google Books API

Utilizar prioritariamente para identificação e informações bibliográficas dos livros.

### Open Library API

Utilizar para complementar informações sobre obras, edições, ISBN e autores.

### Wikidata

Utilizar para identificação de autores, nacionalidades, cidadanias e informações geográficas das obras.

### Regras de integração

- Todas as consultas externas devem ser realizadas pelo backend.
- Não chamar essas APIs diretamente pelo frontend.
- Implementar cache.
- Armazenar identificadores externos.
- Evitar duplicação de registros.
- Registrar a fonte de cada informação.
- Permitir correção manual.
- Implementar tratamento de indisponibilidade das fontes.
- Não depender exclusivamente de uma API.
- Respeitar limites de requisição e condições de uso de cada serviço.
- Verificar licenciamento e atribuição antes de redistribuir dados ou imagens.

Não inventar informações ausentes.

Quando houver divergência entre fontes, preservar a procedência e aplicar critérios explícitos de prioridade e confiança.

O cadastro deve exigir o mínimo possível de preenchimento manual.

## 7. Informações geográficas e países

Utilizar ISO 3166-1 alpha-3 como identificador geográfico padronizado dos países.

Manter uma tabela de países com:

- Id.
- Name.
- IsoCode.
- Continent.
- GeoJsonId.

Não utilizar nomes de países como identificadores de relacionamento.

O sistema deverá permitir múltiplas relações geográficas por autor e obra.

Cada relação geográfica deverá preservar:

- País.
- Fonte.
- Tipo de relacionamento.
- Nível de confiança.
- Status de verificação.

Dados não encontrados devem permanecer como desconhecidos, nunca ser interpretados automaticamente como ausência de nacionalidade ou origem.

## 8. Mapas literários

O mapa é a principal funcionalidade visual do produto.

### Identidade visual

- Tema escuro.
- Fundo azul-marinho ou preto.
- Países não explorados em tonalidades escuras.
- Países explorados em tonalidades progressivamente mais claras.
- Bordas discretas.
- Interface minimalista.
- Animações suaves.

### Interação

Ao passar o mouse ou selecionar um país:

- Nome do país.
- Quantidade de livros.
- Quantidade de autores, quando aplicável.
- Lista de livros relacionados.

A escala de preenchimento deverá considerar a quantidade de livros associados, preferencialmente com normalização para evitar distorções causadas por países com volumes muito elevados.

O mapa deverá ser responsivo.

Não acoplar regras de negócio ao componente de visualização geográfica.

A camada de mapas deverá receber dados estruturados e independentes da biblioteca visual utilizada.

## 9. Passaportes Literários

A aplicação deverá apresentar estatísticas de exploração literária.

Indicadores:

- Países explorados através dos autores.
- Países explorados através das obras.
- Países distintos considerando os dois mapas.
- Continentes explorados.
- Países com maior quantidade de livros.
- Evolução histórica das leituras.

Os indicadores devem considerar somente livros concluídos.

## 10. Compartilhamento

Permitir gerar imagens dos mapas literários.

As imagens poderão conter:

- Mapa-múndi.
- Países destacados.
- Nome ou apelido do usuário.
- Quantidade de livros.
- Quantidade de países explorados.
- Identificação do tipo de mapa.
- Identidade visual do Atlas Literário.

Respeitar as configurações de privacidade.

Não tornar pública a biblioteca do usuário automaticamente.

Os mapas são feitos para serem compartilhados: quem encontrar o perfil de um usuário (busca por apelido) poderá ver os mapas dele **sem precisar segui-lo**, desde que o perfil esteja visível (configuração de privacidade do usuário). Em um perfil **público**, o mapa é exibido por completo: países, autores, títulos dos livros concluídos e as resenhas. Como isso expõe conteúdo pessoal, a visibilidade pública só vale por **escolha explícita** do usuário; o padrão para contas existentes e novas é privado (ver docs/business-rules.md §5). O link de compartilhamento (`LinkOnly`) e as imagens geradas continuam expondo apenas dados agregados.

## 11. Contratos entre frontend e backend

A API REST deverá ser a única forma de comunicação entre as aplicações.

Regras:

- Definir DTOs específicos.
- Documentar endpoints no Swagger/OpenAPI.
- Padronizar respostas.
- Padronizar tratamento de erros.
- Utilizar paginação em listagens.
- Implementar validações.
- Evitar retornar informações desnecessárias.
- Não compartilhar entidades do domínio com o frontend.

O frontend deverá manter seus tipos TypeScript compatíveis com os contratos da API.

Quando possível, avaliar geração automática dos tipos a partir do OpenAPI.

Documentar mudanças incompatíveis antes de implementá-las.

## 12. Segurança

- Não armazenar senhas em texto puro.
- Utilizar hash seguro de senhas.
- Proteger endpoints privados.
- Validar permissões do usuário em todas as operações da biblioteca.
- Não permitir acesso a recursos privados de outros usuários.
- Não expor tokens ou credenciais.
- Utilizar variáveis de ambiente para configurações sensíveis.
- Não versionar arquivos com segredos.
- Validar entradas recebidas pela API.
- Proteger recursos de compartilhamento público.

## 13. Padrões de desenvolvimento

### Gerais

- Priorizar código simples e legível.
- Evitar duplicação.
- Utilizar nomes descritivos.
- Separar responsabilidades.
- Evitar abstrações desnecessárias.
- Não implementar funcionalidades fora do escopo sem justificativa.
- Não adicionar dependências apenas por conveniência.
- Manter documentação atualizada.
- Preferir soluções sustentáveis e fáceis de manter.

### Testes

Criar testes adequados para:

- Regras de negócio.
- Serviços.
- Persistência.
- Endpoints.
- Integrações.
- Componentes e funcionalidades críticas do frontend.

Não considerar uma implementação concluída sem verificar compilação e testes relevantes.

Não afirmar que testes passaram sem executá-los.

## 14. Estrutura de documentação

Manter a pasta `/docs` atualizada.

Arquivos previstos:

- architecture.md: arquitetura geral.
- database.md: modelagem e relacionamentos.
- api-contracts.md: contratos e endpoints.
- development-plan.md: etapas e progresso.
- external-integrations.md: integrações e fontes de dados.
- business-rules.md: regras de negócio.

Atualizar a documentação quando decisões importantes forem alteradas.

## 15. Controle de escopo

O objetivo inicial é desenvolver um MVP funcional.

Prioridades:

1. Autenticação.
2. Biblioteca pessoal.
3. Pesquisa automática de livros.
4. Integrações bibliográficas.
5. Identificação geográfica.
6. Mapa de nacionalidades.
7. Mapa de origem das obras.
8. Dashboard.
9. Passaportes Literários.
10. Compartilhamento.

Etapas planejadas (ver docs/development-plan.md):

- Etapa 5: Dashboard, Passaportes Literários e **seguir/seguidores** (relação assimétrica: seguir e "seguir de volta"; **sem** conceito de amizade; perfil do usuário lista seguindo e seguidores).
- Etapa 6: Compartilhamento e finalização; **feed** com resenhas e atualizações do histórico de leitura fica **em aberto** (decidir se entra, com seu modelo de privacidade, antes de implementar).
- Etapa 7: estatísticas de leitura por ano.

Não implementar inicialmente (salvo o que as etapas acima liberam):

- Amizade (relação mútua com aceite).
- Mensagens privadas.
- Rankings globais.
- Comentários públicos.
- Recomendações complexas.
- Gamificação avançada.

Evitar overengineering.

## 16. Processo de trabalho

Antes de iniciar qualquer implementação significativa:

1. Entender o contexto da funcionalidade.
2. Verificar a arquitetura existente.
3. Identificar os módulos afetados.
4. Verificar os contratos.
5. Planejar a alteração.
6. Implementar.
7. Executar testes.
8. Atualizar documentação quando necessário.

Antes de modificar arquivos, verificar se existem alterações locais não relacionadas e preservá-las.

Não sobrescrever trabalho de outro agente.

Não realizar commits ou publicar alterações sem autorização explícita do usuário.

## 17. Comunicação e acompanhamento

O agente principal deverá apresentar periodicamente:

- Etapa atual.
- Funcionalidades implementadas.
- Agentes envolvidos.
- Testes executados.
- Problemas encontrados.
- Decisões pendentes.
- Próximas atividades.

Ao concluir uma tarefa, informar objetivamente o que foi alterado e quais validações foram realizadas.

Quando houver dúvidas arquiteturais relevantes, apresentar alternativas e justificar a recomendação antes de tomar decisões que impactem todo o projeto.

## 18. Diretriz principal

O Atlas Literário deve ser desenvolvido com foco em uma experiência de leitura visualmente envolvente, arquitetura sustentável e dados geográficos confiáveis.

O mapa-múndi é o principal diferencial do produto e deve orientar as decisões de experiência do usuário.

Priorizar consistência, simplicidade e qualidade.

Não sacrificar a arquitetura por velocidade de implementação, mas também não criar complexidade desnecessária para um MVP.

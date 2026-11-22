import assert from 'node:assert/strict';
import { test } from 'node:test';
import { authorScore, decide, splitAuthors, titleScore } from '../src/match.mjs';
import { cleanIsbn, normalize } from '../src/text.mjs';

const cand = (o) => ({ resultKey: `${o.provider ?? 'GoogleBooks'}:${o.title}:${o.publisher ?? ''}`, bookId: null, inLibrary: false, libraryItemId: null, subtitle: null, authors: [], publisher: null, isbn13: null, source: { provider: o.provider ?? 'GoogleBooks', externalId: o.title }, ...o });
const row = (o) => ({ row: 2, title: '', author: '', publisher: '', isbn: null, ...o });

test('normalize remove acentos, pontuação e caixa', () => {
  assert.equal(normalize('  Cem Anos de Solidão! '), 'cem anos de solidao');
  assert.equal(normalize('Pride & Prejudice'), 'pride e prejudice');
});

test('cleanIsbn aceita ISBN-13/10 válidos e rejeita dígito errado', () => {
  assert.equal(cleanIsbn('978-0-306-40615-7'), '9780306406157');
  assert.equal(cleanIsbn('0-306-40615-2'), '9780306406157');
  assert.equal(cleanIsbn('9780306406158'), null);
  assert.equal(cleanIsbn('abc'), null);
});

test('título: igual, com subtítulo, com artigo e diferente', () => {
  assert.equal(titleScore('Dom Casmurro', 'Dom Casmurro'), 1);
  assert.ok(titleScore('Dom Casmurro', 'Dom Casmurro', 'romance') >= 0.99);
  assert.ok(titleScore('O Processo', 'Processo') >= 0.99);
  assert.ok(titleScore('Dom Casmurro', 'Dom Casmurro: edição comentada') >= 0.85);
  assert.ok(titleScore('Dom Casmurro', 'Memórias Póstumas de Brás Cubas') < 0.3);
});

test('autor: ordem invertida, iniciais, sobrenome composto e desconhecido', () => {
  assert.equal(authorScore(['Assis, Machado de'], ['Machado de Assis']), 1);
  assert.ok(authorScore(['J. R. R. Tolkien'], ['John Ronald Reuel Tolkien']) >= 0.9);
  assert.ok(authorScore(['Gabriel Garcia Marquez'], ['Gabriel García Márquez']) === 1);
  assert.ok(authorScore(['Márquez'], ['Gabriel García Márquez']) >= 0.8);
  assert.ok(authorScore(['Franz Kafka'], ['Albert Camus']) < 0.5);
  assert.equal(authorScore([], ['X']), null);
  assert.equal(authorScore(['Franz Kafka'], []), 0.3);
});

test('splitAuthors separa ; / & e " e "', () => {
  assert.deepEqual(splitAuthors('Machado de Assis; Lima Barreto'), ['Machado de Assis', 'Lima Barreto']);
  assert.deepEqual(splitAuthors('Deleuze & Guattari'), ['Deleuze', 'Guattari']);
  assert.deepEqual(splitAuthors('Jorge Amado e Zélia Gattai'), ['Jorge Amado', 'Zélia Gattai']);
});

test('decide: aceita o acerto óbvio e escolhe a edição da editora informada', () => {
  const d = decide(row({ title: 'Dom Casmurro', author: 'Machado de Assis', publisher: 'Ática' }), [
    cand({ title: 'Dom Casmurro', authors: ['Machado de Assis'], publisher: 'Penguin', isbn13: '9780000000002' }),
    cand({ title: 'Dom Casmurro', authors: ['Machado de Assis'], publisher: 'Editora Ática', isbn13: '9780000000019' }),
  ]);
  assert.equal(d.outcome, 'match');
  assert.equal(d.pick.cand.publisher, 'Editora Ática');
});

test('decide: duas obras diferentes igualmente prováveis => ambíguo', () => {
  const d = decide(row({ title: 'Ensaio sobre a', author: 'José Saramago' }), [
    cand({ title: 'Ensaio sobre a Cegueira', authors: ['José Saramago'] }),
    cand({ title: 'Ensaio sobre a Lucidez', authors: ['José Saramago'] }),
  ]);
  assert.equal(d.outcome, 'ambiguous');
  assert.equal(d.alternatives.length, 2);
});

test('decide: autor errado não é aceito mesmo com título idêntico', () => {
  const d = decide(row({ title: 'O Processo', author: 'Franz Kafka' }), [cand({ title: 'O Processo', authors: ['Fulano de Tal'] })]);
  assert.notEqual(d.outcome, 'match');
});

test('decide: sem autor só aceita título praticamente idêntico', () => {
  assert.equal(decide(row({ title: 'O Processo' }), [cand({ title: 'O Processo', authors: ['Franz Kafka'] })]).outcome, 'match');
  assert.notEqual(decide(row({ title: 'Processo' }), [cand({ title: 'O Processo Civil Brasileiro Comentado', authors: ['X'] })]).outcome, 'match');
});

test('decide: ISBN igual vence a comparação de texto', () => {
  const d = decide(row({ title: 'qualquer coisa', isbn: '9780306406157' }), [cand({ title: 'Outro Nome', authors: ['Alguém'], isbn13: '9780306406157' })]);
  assert.equal(d.outcome, 'match');
});

test('decide: sem resultados => notfound; resultado fraco => lowconf', () => {
  assert.equal(decide(row({ title: 'Xyz', author: 'Abc' }), []).outcome, 'notfound');
  const d = decide(row({ title: 'Dom Casmurro', author: 'Machado de Assis' }), [cand({ title: 'Dom Casmurro', authors: ['Outro Autor'] })]);
  assert.equal(d.outcome, 'lowconf');
});

test('decide: prefere livro já no catálogo/na biblioteca em empate de edição', () => {
  const d = decide(row({ title: 'Dom Casmurro', author: 'Machado de Assis' }), [
    cand({ title: 'Dom Casmurro', authors: ['Machado de Assis'], provider: 'OpenLibrary' }),
    cand({ title: 'Dom Casmurro', authors: ['Machado de Assis'], provider: 'Local', bookId: 'b1', inLibrary: true, source: { provider: 'Local', externalId: 'b1' } }),
  ]);
  assert.equal(d.pick.cand.inLibrary, true);
});

test('splitAuthors: vírgula separa autores só quando cada parte tem nome e sobrenome', () => {
  assert.deepEqual(splitAuthors('Karl Marx, Friedrich Engels'), ['Karl Marx', 'Friedrich Engels']);
  assert.deepEqual(splitAuthors('Assis, Machado de'), ['Assis, Machado de']);
  assert.deepEqual(splitAuthors('Jones Manoel, Gabriel Landi (org.)'), ['Jones Manoel', 'Gabriel Landi']);
});

test('"(adaptation)" no candidato não vira o livro original; edições com vírgula/autor variante são a mesma obra', () => {
  const adaptation = decide(row({ title: '1984', author: 'George Orwell' }), [cand({ title: '1984 (adaptation)', authors: ['Michael Dean', 'George Orwell'] })]);
  assert.notEqual(adaptation.outcome, 'match');
  const d = decide(row({ title: 'Vidas secas', author: 'Graciliano Ramos' }), [
    cand({ title: 'Vidas Secas', authors: ['Graciliano Ramos'], resultKey: 'a' }),
    cand({ title: 'Vidas sêcas, romance', authors: ['Graciliano Ramos de Oliveira'], resultKey: 'b' }),
  ]);
  assert.equal(d.outcome, 'match');
});

test('título só em outro idioma: autor confere => sugestão (lowconf), nunca importa sozinho', () => {
  const d = decide(row({ title: 'O Sol e a Estrela', author: 'Rick Riordan, Mark Oshiro' }), [cand({ title: 'The Sun and the Star', authors: ['Rick Riordan', 'Mark Oshiro'] })]);
  assert.equal(d.outcome, 'lowconf');
  assert.equal(d.alternatives.length, 1);
});

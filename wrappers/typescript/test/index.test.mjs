import { test } from 'node:test';
import assert from 'node:assert/strict';
import { WorkflowBuilder, ref } from '../dist/index.js';

test('serializes plans and nested result references in the shared format', () => {
  const json = new WorkflowBuilder()
    .addStep('lookup', 'catalog.lookup', { key: 'a1' })
    .addStep('format', 'text.format', { record: { nested: ref('lookup') } })
    .toJSON('format');
  assert.deepEqual(JSON.parse(json), {
    steps: [
      { id: 'lookup', tool: 'catalog.lookup', arguments: { key: 'a1' } },
      { id: 'format', tool: 'text.format', arguments: { record: { nested: { $ref: 'lookup' } } } },
    ], output: 'format',
  });
});

test('rejects a forward or missing reference', () => {
  const builder = new WorkflowBuilder();
  assert.throws(() => builder.addStep('first', 'tool', { value: ref('later') }), /earlier step/);
});

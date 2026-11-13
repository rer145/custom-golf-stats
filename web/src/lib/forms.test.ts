import { describe, expect, it } from 'vitest';
import { formText, notAForm, readForm } from './forms';

const post = (body: BodyInit | null, contentType?: string) =>
  new Request('http://x.test/sign-in', { method: 'POST', body, ...(contentType ? { headers: { 'content-type': contentType } } : {}) });

describe('readForm', () => {
  it('reads urlencoded and multipart forms', async () => {
    const urlencoded = await readForm(post('email=a%40b.test&password=pw', 'application/x-www-form-urlencoded'));
    expect(urlencoded?.get('email')).toBe('a@b.test');
    const fd = new FormData();
    fd.set('email', 'c@d.test');
    const multipart = await readForm(new Request('http://x.test/', { method: 'POST', body: fd }));
    expect(multipart?.get('email')).toBe('c@d.test');
  });

  it('returns null for JSON, text, missing content type or a malformed multipart body', async () => {
    expect(await readForm(post('{"email":"a@b.test"}', 'application/json'))).toBeNull();
    expect(await readForm(post('email=a', 'text/plain'))).toBeNull();
    expect(await readForm(post(null))).toBeNull();
    expect(await readForm(post('garbage', 'multipart/form-data; boundary=xyz'))).toBeNull();
  });
});

describe('formText', () => {
  it('returns the string value, or empty for missing and file values', () => {
    const fd = new FormData();
    fd.set('name', 'Pat');
    fd.set('upload', new Blob(['x']), 'x.txt');
    expect(formText(fd, 'name')).toBe('Pat');
    expect(formText(fd, 'upload')).toBe('');
    expect(formText(fd, 'missing')).toBe('');
  });
});

describe('notAForm', () => {
  it('is a 415 plain-text response', async () => {
    const res = notAForm();
    expect(res.status).toBe(415);
    expect(await res.text()).toMatch(/form/);
  });
});

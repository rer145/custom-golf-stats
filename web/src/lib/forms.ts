/**
 * Reading HTML form posts in pages. Use these instead of `Astro.request.formData()`, which throws (a 500) on JSON,
 * text or malformed bodies:
 *
 *   const form = await readForm(Astro.request);
 *   if (!form) return notAForm();
 *   const email = formText(form, 'email');
 */
const FORM_TYPES = ['application/x-www-form-urlencoded', 'multipart/form-data'];

/** The posted form, or `null` when the body isn't a (well-formed) urlencoded or multipart form. */
export async function readForm(request: Request): Promise<FormData | null> {
  const type = (request.headers.get('content-type') ?? '').split(';')[0]!.trim().toLowerCase();
  if (!FORM_TYPES.includes(type)) return null;
  try {
    return await request.formData();
  } catch {
    return null;
  }
}

/** A field's text value; '' when it is missing or a file. */
export function formText(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value : '';
}

/** 415 for a POST whose body isn't a form (only scripts send those; the site's own forms never do). */
export function notAForm(): Response {
  return new Response('Expected an HTML form submission.', { status: 415, headers: { 'content-type': 'text/plain; charset=utf-8' } });
}

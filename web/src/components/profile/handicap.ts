/**
 * Reading a handicap index as golfers write it. A plus handicap is written `+1.2` and stored as −1.2 (the API's
 * range is −10.0 to 54.0, one decimal); a leading minus means the same. Range and precision are left to the API.
 */
export function parseHandicapIndex(input: string): { value: number | null; error?: string } {
  const text = input.trim().replace(/−/g, '-');
  if (text === '') return { value: null, error: 'A handicap index is required.' };
  const match = /^([+-]?)(\d+(?:\.\d*)?|\.\d+)$/.exec(text);
  if (!match) return { value: null, error: 'Enter a number, like 12.4 (or +1.2 for a plus handicap).' };
  const magnitude = Number(match[2]);
  return { value: match[1] === '' ? magnitude : -magnitude };
}

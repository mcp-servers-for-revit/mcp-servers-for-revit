interface NextStep {
  prompt: string;
  reason: string;
}

export function addSuggestions(response: any, suggestions: (NextStep | null)[]): any {
  const filtered = suggestions.filter((s): s is NextStep => s !== null);
  if (filtered.length === 0) return response;
  return { ...response, suggestedNextSteps: filtered };
}

export function suggestIf(condition: boolean, prompt: string, reason: string): NextStep | null {
  return condition ? { prompt, reason } : null;
}

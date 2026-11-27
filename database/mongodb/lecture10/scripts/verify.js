// Your implementation. This function receives only successful query results.
// actual is a document or null; expected contains the four fields below.
// Return exactly: PresentMatches, PresentConflicts, or AbsentAtVerification.
// Compare _id, tripId, status and sourceVersion. Do not query or write here.
const COMPARED_FIELDS = ['_id', 'tripId', 'status', 'sourceVersion'];

function compareChange(actual, expected) {
  // A successful query that found nothing. Read failures never reach this function.
  if (actual === null) return 'AbsentAtVerification';

  // Strict equality: a missing field (undefined) or a different type ("2" vs 2)
  // counts as a conflict, never as a match.
  for (const field of COMPARED_FIELDS) {
    if (!Object.prototype.hasOwnProperty.call(actual, field)) return 'PresentConflicts';
    if (actual[field] !== expected[field]) return 'PresentConflicts';
  }
  return 'PresentMatches';
}

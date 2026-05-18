# Timetable PDF Source Tokens

This companion document lists human-readable CQEPC source tokens referenced by the timetable PDF parser contract. The runtime source of truth remains `CQEPC.TimetableSync.Infrastructure/Parsing/Pdf/TimetablePdfLexicon.cs`.

Keep parser logic ASCII-safe where possible. Add exact CQEPC Chinese labels to the lexicon and update this document when a source-token assumption changes.

## Core layout labels

- class header suffix: `课表`
- practical summary prefix: `实践课程`
- print timestamp prefix: `打印时间:`
- period lead example: `(1-2节)`

## Tagged metadata labels

- `/校区:`
- `/场地:`
- `/教师:`
- `/教学班组成:`
- `/教学班:`
- `/教学班人数:`
- `/考核方式:`
- `/课程学时组成:`
- `/学分:`

## Course type markers

- `★` => theory
- `☆` => lab
- `◆` => practical
- `■` => computer
- `〇` => extracurricular

## Alias contract

`教学班:` is the short alias of `教学班组成:`. When the source contains a sequence such as `教师:.../教学班:...`, the parser must split it into:

- `Teacher`;
- `TeachingClassComposition`;
- remaining tail metadata in `Notes`.

The parser must not leave `/教学班:...` attached to the teacher field.

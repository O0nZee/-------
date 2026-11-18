"""Export current SQLite student profiles to a local SQL snapshot."""

import argparse
from pathlib import Path
import sqlite3
import tempfile


COLUMNS = (
    'id', 'username', 'email', 'full_name', 'department', 'phone',
    'academic_identifier', 'bio', 'is_active', 'created_at', 'updated_at',
)

EXPORT_SCHEMA = '''CREATE TABLE IF NOT EXISTS student_records (
    id TEXT PRIMARY KEY,
    username TEXT NOT NULL UNIQUE COLLATE NOCASE,
    email TEXT NOT NULL UNIQUE COLLATE NOCASE,
    full_name TEXT,
    department TEXT,
    phone TEXT,
    academic_identifier TEXT,
    bio TEXT,
    is_active INTEGER NOT NULL CHECK(is_active IN (0, 1)),
    created_at TEXT,
    updated_at TEXT
);'''


def sql_literal(value):
    if value is None:
        return 'NULL'
    if isinstance(value, int):
        return str(value)
    text = str(value)
    if '\0' in text:
        return "CAST(X'" + text.encode('utf-8').hex() + "' AS TEXT)"
    return "'" + text.replace("'", "''") + "'"


def export_students(database, output):
    database = Path(database).resolve()
    output = Path(output).resolve()
    if not database.is_file():
        raise ValueError('Database does not exist. Start the platform first.')
    if output.suffix.lower() != '.sql' or output.name.lower() == 'schema.sql':
        raise ValueError('Choose a SQL snapshot filename other than schema.sql.')
    if not output.parent.is_dir():
        raise ValueError('Output directory does not exist.')
    # Read one consistent snapshot without changing the application records.
    source = sqlite3.connect(database.as_uri() + '?mode=ro', uri=True, timeout=10)
    try:
        source.execute('BEGIN')
        rows = source.execute('SELECT ' + ', '.join(COLUMNS) + ' FROM students ORDER BY id').fetchall()
    finally:
        source.close()
    column_list = ', '.join(COLUMNS)
    updates = ', '.join(name + '=excluded.' + name for name in COLUMNS if name != 'id')
    lines = [
        '-- Local SQLite snapshot of student profiles, not an account restore.',
        '-- No passwords, authentication hashes, salts or reset tokens.',
        '-- Import into a separate SQLite database to inspect these records.',
        '-- Student records: ' + str(len(rows)),
        'BEGIN TRANSACTION;', EXPORT_SCHEMA,
    ]
    for row in rows:
        lines.append('INSERT INTO student_records (' + column_list + ') VALUES ('
                     + ', '.join(sql_literal(value) for value in row)
                     + ') ON CONFLICT(id) DO UPDATE SET ' + updates + ';')
    lines.extend(['COMMIT;', ''])
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode='w', encoding='utf-8', newline='\n',
                                         dir=output.parent, prefix=output.name + '.',
                                         suffix='.tmp', delete=False) as stream:
            temporary = Path(stream.name)
            stream.write('\n'.join(lines))
        temporary.replace(output)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)
    return len(rows)


def main():
    directory = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--database', type=Path, default=directory.parent / 'data/graduation.db')
    parser.add_argument('--output', type=Path, default=directory / 'students-data.sql')
    args = parser.parse_args()
    try:
        count = export_students(args.database, args.output)
    except (OSError, ValueError, sqlite3.Error) as error:
        parser.exit(1, f'Export failed: {error}\n')
    print(f'Exported {count} student profiles to {args.output.resolve()}')


if __name__ == '__main__':
    main()

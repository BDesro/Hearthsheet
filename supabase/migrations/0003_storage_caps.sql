-- Hearthsheet storage caps. Run once after 0002 in the Supabase SQL editor (see docs/CLOUD_SYNC.md).
-- 0002 capped each document, but name and description had no limit and 100 rows x 1 MB is 100 MB per user, so one
-- account could still fill the 500 MB free database.

-- Both are derived from the document (the character's name; species and classes). Real ones are under 40 characters.
alter table public.characters add constraint characters_text_size
  check (char_length(name) <= 1000 and char_length(description) <= 2000);

-- At most 100 rows and 10 MB of documents per user (about 600 real 17 KB characters; ten users stay under 100 MB).
-- Now also runs on update, since an update can grow a document. characters_bump runs first (triggers fire in name
-- order), so new.user_id is already the row's owner. The lock serializes concurrent writes by one user.
create or replace function public.characters_cap() returns trigger
language plpgsql set search_path = '' as $$
begin
  perform pg_advisory_xact_lock(hashtextextended(new.user_id::text, 0));
  if tg_op = 'INSERT' and (select count(*) from public.characters where user_id = new.user_id) >= 100 then
    raise exception 'Character limit reached (100). Delete some characters or ask Beau.' using errcode = 'P0001';
  end if;
  if coalesce(octet_length(new.data::text), 0)
     + (select coalesce(sum(octet_length(data::text)), 0) from public.characters where user_id = new.user_id and id <> new.id)
     > 10485760 then
    raise exception 'Storage limit reached (10 MB). Delete some characters or ask Beau.' using errcode = 'P0001';
  end if;
  return new;
end $$;

drop trigger characters_cap on public.characters;
create trigger characters_cap before insert or update on public.characters
  for each row execute function public.characters_cap();

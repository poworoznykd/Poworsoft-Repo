-- CollectIQ private card image storage
-- Run once in Supabase SQL Editor before testing image sync.

insert into storage.buckets (id, name, public)
values ('card-images', 'card-images', false)
on conflict (id) do update
set public = false;

drop policy if exists "CollectIQ card images select" on storage.objects;
drop policy if exists "CollectIQ card images insert" on storage.objects;
drop policy if exists "CollectIQ card images update" on storage.objects;
drop policy if exists "CollectIQ card images delete" on storage.objects;

create policy "CollectIQ card images select"
on storage.objects
for select
to authenticated
using (
    bucket_id = 'card-images'
    and (storage.foldername(name))[1] = (select auth.jwt()->>'sub')
);

create policy "CollectIQ card images insert"
on storage.objects
for insert
to authenticated
with check (
    bucket_id = 'card-images'
    and (storage.foldername(name))[1] = (select auth.jwt()->>'sub')
);

create policy "CollectIQ card images update"
on storage.objects
for update
to authenticated
using (
    bucket_id = 'card-images'
    and (storage.foldername(name))[1] = (select auth.jwt()->>'sub')
)
with check (
    bucket_id = 'card-images'
    and (storage.foldername(name))[1] = (select auth.jwt()->>'sub')
);

create policy "CollectIQ card images delete"
on storage.objects
for delete
to authenticated
using (
    bucket_id = 'card-images'
    and (storage.foldername(name))[1] = (select auth.jwt()->>'sub')
);

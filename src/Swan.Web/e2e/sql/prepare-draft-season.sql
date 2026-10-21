-- For a DEDICATED e2e database only. Gives the roster-draft e2e what the real schedule import would: an 18-week schedule for the season currently in Setup
-- (the BALLDONTLIE import needs a key and the network). Run it after the Setup season exists.

insert into schedule.weeks ("Id", "SeasonId", "Number", "Type", "State")
select gen_random_uuid(), s."Id", n, '0||True', 0
from league.seasons s, generate_series(1, 18) n
where s."State" = 0;

insert into schedule.games ("Id", "HomeTeamId", "AwayTeamId", "Kickoff", "Excluded", "WeekId", "LineHistory", "ProviderGameId")
select gen_random_uuid(),
       (select "Id" from schedule.teams order by "Name" offset game.number limit 1),
       (select "Id" from schedule.teams order by "Name" offset game.number + 16 limit 1),
       make_timestamptz(s."Year", 9, 9, 17, 0, 0, 'UTC') + (w."Number" - 1) * interval '7 days' + game.number * interval '1 hour',
       false, w."Id", ARRAY[]::numeric[], 'e2e-draft-' || s."Year" || '-' || w."Number" || '-' || game.number
from schedule.weeks w
join league.seasons s on s."Id" = w."SeasonId"
cross join generate_series(0, 15) game(number)
where s."State" = 0;

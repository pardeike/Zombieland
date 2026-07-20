# DistroKid Submission Checklist

The account owner must complete account creation, payment, tax, and the final
legal attestations. Everything else is prepared in this directory.

## Release order

1. Upload **Zombieland - Calm** first as a new artist named `Brrainz`.
2. When DistroKid supplies the Spotify artist URI/link, use the existing Spotify
   session to claim the Brrainz profile in Spotify for Artists.
3. Upload **Zombieland - Tense** and map it explicitly to that claimed Brrainz
   Spotify artist URI. Do not create a second new Brrainz artist entry.

This ordering prevents Spotify from splitting the two albums across similarly
named artist pages.

## Account creation: owner actions

1. Open [DistroKid](https://distrokid.com/) in Safari and choose a single-artist
   distribution plan. Do not add optional extras yet.
2. Sign up with `info@brrai.nz` and complete email verification.
3. Enter the legal payout and tax information in your own name.
4. Stop before any payment or final submission if you want the agent to perform
   the non-financial upload steps in the Safari session.

## Calm upload

Use `METADATA.md` as the source of truth.

1. Choose **Upload music** then **Album**.
2. Select Spotify, Apple Music, YouTube Music, Amazon Music, Deezer, and Tidal.
3. Artist: `Brrainz`; this is a **new artist** for the first upload.
4. Album: `Zombieland - Calm`; label: `Brrainz`.
5. Set genre to Ambient, with Soundtrack as secondary where offered.
6. Upload `artwork/zombieland-calm-cover.jpg`.
7. Upload the 22 WAVs in the numbered order in `METADATA.md`.
8. Set each public title exactly as listed; do not include the repeated source
   filename prefix `Zombieland –`.
9. Use the legal songwriter name and correct ownership answer. All works were
   made under Suno Pro and are commercially distributable by Brrainz.
10. In AI credits, identify AI-generated music/audio for every track. Identify
    AI-generated lyrics only for tracks where Suno supplied the lyrics.
11. Do not opt into YouTube Content ID, social-media fingerprinting, or paid
    promotional extras at launch.
12. Choose a release date at least three weeks ahead if possible, then save as a
    draft for a final on-screen review before submitting.

## Claim Spotify for Artists

After DistroKid shows the Spotify artist URI for the upcoming Calm release:

1. In the existing Spotify account, visit [Spotify for Artists](https://artists.spotify.com/).
2. Claim the Brrainz artist profile using the URI and any requested release UPC.
3. Add the bio from `METADATA.md`, the Calm cover as profile imagery if desired,
   and `https://brrai.nz/` as the public website.

## Tense upload

Repeat the Calm process with these changes:

- album: `Zombieland - Tense`;
- genres: Soundtrack, then Electronic where offered;
- artwork: `artwork/zombieland-tense-cover.jpg`;
- upload the 20 masters in the Tense order from `METADATA.md`;
- choose **existing artist** and paste/select the claimed Brrainz Spotify URI.

## Final release check

Before submitting each album, confirm the visible review page shows:

- Artist: `Brrainz`
- Correct album title and 22/20 track count
- Correct original artwork, with no text, logos, or copyright warnings
- No Content ID/social fingerprinting add-on
- A future date appropriate for the desired launch
- Correct AI disclosures and non-explicit status

# Asetukset, äänet kuvina ja vihjeet epäillyistä (vaihe 5d)

Käyttäjä pyysi 10.10.2026 kahta asiaa:
- **Vihjeteksti epäiltyjen päälle.** Kun hiiri on kohteen, kuten epäillyn vihollisen, päällä, pitää näkyä tietoa.
- **Äänet kuvina.** Äänien pitää voida näkyä myös grafiikkana, jos haluaa pelata äänettömänä.

Äänimerkit kytketään päälle pikanäppäimellä ja asetusikkunasta. Asetusikkuna on äänieepoksen vaihe 5d
(spec 2026-10-09-sound-design), joten se tehdään nyt samalla.

## 1. Vihje epäillyn ja viimeksi nähdyn päällä

Nyt vihje (`EnemyInfo`) tulee vain nähdyn vihollisen päälle. Uusi `ContactInfo.Describe` (Nmf.Client) kuvaa merkin,
jonka päällä hiiri on. Merkki on epäilty "?" tai viimeksi nähdyn haamu (`GameSession.ContactMarkAt`).

**Epäilty (kuultu):**
- *"Kuultu vihollinen: laukauksia · noin 85 m · 12 s sitten"*
- *"Kuultu vihollinen: liikettä · noin 40 m · 3 s sitten"*

**Viimeksi nähty:**
- *"Viimeksi nähty 40 s sitten: kiväärimies, Mosin 91/30 · noin 60 m"*
- Rooli ja ase ovat samat kuin `EnemyInfo`ssa. Mies on nähty, joten ne ovat puolen tietoa.

Etäisyys on lähimmästä omasta taistelukykyisestä miehestä merkkiin, ja se pyöristetään 5 metriin. Aika on pelin aikaa
kontaktin viimeisestä päivityksestä.

**Laukaus vai liike.** Simulaation `Contact` saa kentän `HeardShots`. `VisionSystem.UpdateHearing` asettaa sen, kun
kuultiin laukaus, ja tyhjentää, kun kuultiin vain liikettä. Kenttä lisätään `StateHash`iin. Ilman tätä kenttää vihje
kertoisi asian, jota miehet eivät tiedä.

Vihje käyttää samaa viivettä ja paneelia kuin nykyinen vihje. Nähty vihollinen menee aina merkin edelle.

## 2. Äänet kuvina

`AudioDirector` päättää jo, mitä pelaajan puoli kuulee. Se kertoo nyt kuullut äänet myös silloin, kun äänitiedostoa ei
ole tai äänet ovat pois: `Update` palauttaa vihjeiden lisäksi listan `HeardSound(Kind, At, Loud)`. Kind on yksi
seuraavista:
- `Shot`: laukaus, myös kaukaa
- `Crack`: luoti lensi läheltä
- `Explosion`
- `Fall`
- `Melee`.

Omia laukauksia ja hiljaisia ääniä (askeleet, lippaat, ryöstö) ei piirretä.

Kun äänimerkit ovat päällä, `SoundMarks` (Nmf.Client) pitää jokaisesta kuullusta äänestä lyhytaikaisen merkin:
- **Paikka:** kuulopaikka, eli piilossa olevalle ampujalle 10 metrin ruudun keskipiste kuten äänissä.
- **Kesto:** 1,5 s, ja `Explosion` 2,5 s.
- **Yhdistäminen:** samanlaiset merkit 10 metrin sisällä yhdistyvät yhdeksi, joka virkistyy, kuten nykyiset signaalit.

Merkit piirretään kaikilla zoomaustasoilla kiinteän kokoisina ruudulla. Laukaukset ovat kaaria "))" ampujan
suuntaan, räjähdykset tähtiä ja luodin sivallukset viivoja omien miesten luona. Kaukozoomissa nykyiset signaalit
näkyvät kuten ennenkin.

Merkit kulkevat pelin ajassa, eli ne pysähtyvät tauolla. Automaattiajot (demo, kuvakaappaus) piirtävät ne, jos ne
ovat päällä.

**Päälle ja pois:**
- pikanäppäin **V** vaihtaa tilaa, ja ilmoitus kertoo uuden tilan ("Äänet kuvina: päällä")
- asetus "Näytä äänet kuvina"
- oletuksena pois, paitsi kun äänet ovat pois: `--mute`, tai päävoimakkuus 0 tai mykistys asetuksissa.

**Äänet pois ilman soittoa.** `AudioView` luodaan aina, myös `--mute`-valinnalla, jotta kuuleminen lasketaan.
Mykistys tehdään Master-väylällä. Automaattiajoissa äänet ovat edelleen pois.

## 3. Asetusikkuna

`GameSettings` (Nmf.Client) on puhdas tietue ja JSON-sarjoitus. Tallennus tehdään tiedostoon `user://settings.json`
samaan tapaan kuin `progress.json` (väliaikainen tiedosto ja siirto paikalleen). Rikkinäinen tai puuttuva tiedosto
antaa oletukset.

| Asetus | Oletus | Vaikutus |
|---|---|---|
| Päävoimakkuus, tehosteet, puhe, ympäristö, musiikki (0–100 %) | 80, 100, 100, 70, 60 | väylien voimakkuus (dB = 20·log10(%)), 0 % = mykistetty |
| Mykistä kaikki | pois | Master-väylä mykistetään |
| Näytä äänet kuvina | pois | kuten V |
| Kieli | suomi, jos järjestelmän kieli on suomi, muuten englanti | valikko ja seuraava tehtävä; `--lang=` ohittaa |
| Koko näyttö | pois | `DisplayServer.WindowSetMode` |
| Käyttöliittymän koko | 100 % (100/125/150) | `Window.ContentScaleFactor` |
| Korttien koko | pieni | `Hud.CardSizeChoice` alussa (K vaihtaa edelleen) |
| Seuraa miehiä alussa | pois | L-tila alussa |

- **Avaaminen:** päävalikon ja pelin Esc-valikon nappi "Asetukset" avaa ikkunan.
- **Toiminta:** muutokset tulevat voimaan heti ja tallentuvat, kun ikkuna suljetaan.
- **Kielen vaihto:** kielivalinta päävalikossa ja asetuksissa on sama asetus. Kesken tehtävän vaihdettu kieli tulee
  voimaan seuraavassa tehtävässä, koska käyttöliittymä on rakennettu valmiiksi.

Näppäinten uudelleenmääritys (5f) ja vaikeustaso (5e) eivät kuulu tähän vaiheeseen. Ikkunassa on niille tila
myöhemmin.

## Testaus

- `ContactInfoTests`:
  - kuultu laukaus ja kuultu liike
  - viimeksi nähty, jossa rooli ja ase
  - etäisyys lähimmästä taistelukykyisestä omasta, pyöristys 5 metriin
  - aika sekunteina
  - fi- ja en-tekstit
- `VisionSystem`: `HeardShots` asettuu laukauksesta ja tyhjenee pelkästä liikkeestä.
- `AudioDirectorTests`:
  - kuullut äänet tulevat myös ilman tiedostoja
  - piilossa olevaa ei tule, kun sitä ei kuulla
  - omia laukauksia ja hiljaisia ääniä ei tule.
- `SoundMarksTests`: kesto, yhdistäminen 10 metrin sisällä, pysähtyminen tauolla (pelin aika).
- `GameSettingsTests`:
  - oletukset
  - JSON edestakaisin
  - rikkinäinen JSON antaa oletukset
  - prosenteista desibeleiksi, 0 % = mykistetty
  - äänet kuvina oletuksena päällä, kun äänet ovat pois.
- Käsin: asetusikkuna päävalikosta ja Esc-valikosta, V-näppäin ja merkit testikentällä kuvakaappauksella.

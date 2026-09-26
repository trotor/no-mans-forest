# No Man's Forest – poterot, ryhmät ryhminä ja ryhmänäppäimet (vaihe 4k)

Päivämäärä: 2026-09-26
Tila: käyttäjän pyyntö ("seuraavaksi poterot kumpareelle; kahden ryhmän ohjaus voisi mennä niin, että ne etenevät eri reittejä tai ainakin tulittavat ja toimivat aina omissa ryhmissään; jotkut ryhmänumeronapit, joilla valitaan ryhmät"), toteutetaan suoraan
Liittyy: ryhmät ja aluetuli `2026-09-26-squads-area-fire-design.md`, hyökkäys `2026-09-26-attack-design.md`, kartta `2026-09-25-real-map-design.md`

## 1. Poterot

**Kartta:**
- Generaattori kaivaa kumpareelle 8 poteroa: yhden jokaisen asemaryhmän miehen kohdalle ja 3 lisää kaaren jatkoksi.
- **Potero** on 1 × 1 m solu, jonka maa on 100 cm ympäristöä alempana. Sen 8 naapurisolua ovat 25 cm korkeampia, koska ne ovat kaivuumaata eli rintavarustusta.
- Kivet ja pensaat raivataan poteron ja varustuksen päältä.
- Kartalle kirjoitetaan pisteet tyyppiä `foxhole` piirtämistä varten.

**Simulaatio:** korkeus hoitaa suurimman osan suojasta:
- **Seisova mies poterossa** näkee ja ampuu reunan yli, mutta reunan yli näkyy vain pää ja olkapäät (noin 45 cm).
  - Häneen tähdätään näkyvän osan keskelle.
  - Osumaleveys on 60 % miehen leveydestä.
  - Hänet havaitaan yhtä hitaasti kuin maassa makaava mies.
  - Katselmoinnissa korjattiin, että aiemmin tähtäys osui reunan alle, jolloin alle noin 40 m päästä ei voinut osua lainkaan.
- **Kyykyssä tai maassa** hän on kokonaan suojassa eikä näe ulos.
- **Poteron tunnistus:** solu on potero, kun sen maa on vähintään 50 cm matalammalla kuin sen matalin neljästä naapurista (`CoverFinder.PitDepthCm`).
- **Suojana:** potero on paras suoja (`CoveredAt` = 230) kaikkiin suuntiin.
  - Tulen alla mies jää poteroonsa ottamaan tuliasennon eikä juokse pois, ja muutkin hakeutuvat lähimpään vapaaseen poteroon.
  - Vihollisen miehittämään poteroon ei hakeuduta.
  - Myös pelaajan siirtokäsky hakeutuu poteroihin.
- **Tuliasento:** poterossa se on seisten, jos sieltä näkee.
  - Kovassakin tulessa mies jatkaa ampumista reunan yli. Pohjalle hän painuu vasta lamautuessaan.
  - Kun lamautus laantuu, hän nousee ampumaan, myös veteraani ja pikakiväärimies, koska poteron pohjalla hän on sokea.
  - Ampumasuunnaksi otetaan nähty vihollinen, aluetulen paikka tai viimeksi nähty vihollinen aseen kantaman sisällä.
  - Ensimmäisessä versiossa mies painui pohjalle jo maahanmenorajalla, jolloin rynnäkkö pääsi perille ilman vastatulta ja potero oli puolustajalle huonompi kuin avoin maasto.

**Näkymä:** potero piirretään tummana kuoppana vaaleamman kaivuumaan keskellä sotilaiden alle.

## 2. Ryhmät ryhminä

- **Siirtokäsky useammalle ryhmälle:** jokainen ryhmä muodostaa oman muodostelmansa.
  - Ryhmien paikat ovat rinnakkain 20 m välein, kohtisuoraan kulkusuuntaan nähden, ja niiden keskikohta on klikattu paikka.
  - Järjestys määräytyy ryhmien nykyisen sijainnin mukaan, jotta reitit eivät ristiin.
  - Jaon saavat vain ryhmät, joista valittuna on vähintään kaksi miestä. Yksittäinen mukaan poimittu mies kulkee muiden mukana.
  - Ryhmät kulkevat siksi eri reittejä.
  - Jos ryhmän paikka on kulkukelvoton tai kartan ulkopuolella, se tulee klikattuun paikkaan.
- **Hyökkäys useammalla ryhmällä:** vuorottelevat puolikkaat ovat ryhmät, eli koko ryhmä etenee tai koko ryhmä tulittaa.
  - Ensin tulittaa ryhmä, jossa on pikakivääri, ja toinen etenee.
  - Yhden ryhmän hyökkäys jakaa miehet kuten ennen.

## 3. Ryhmänäppäimet

- **1, 2, …** valitsevat ryhmän. Kun samaa näppäintä painaa uudelleen 0,4 s sisällä, kamera siirtyy ryhmän kohdalle.
- **0** valitsee koko joukkueen, kuten Esc ja oikea klikkaus.
- **Asennot siirtyvät kirjaimille:** Z seisomaan, X kyykkyyn, C maahan.
- **Korttien ryhmäpainikkeet** näyttävät numeron, esimerkiksi "1 · Iskuryhmä". Valittu ryhmä näkyy painettuna.

## 4. Tasapainokoe

Iskuosasto: 7 vastaan 9, kontakti 100 m, koko joukkue, 8 siementä, 5 min.

| Tapa | Suomalaisia pois taistelusta | Vihollisia pois taistelusta (asema / reservi) |
|---|---|---|
| Tuli- ja liikehyökkäys | 3,5 / 7 | 4,8 / 9 |
| Suora rynnäkkö | 2,4 / 7 | 2,9 / 9 (1 / 1,9) |

Suora rynnäkkö kaataa vain kohteensa ja loppuu siihen. Hyökkäys jatkaa aseman muihin miehiin, joten se on kalliimpi mutta vie kumpareen.

## 5. Testaus

- **Python:**
  - poterot asemaryhmän kohdilla
  - syvyys ja rintavarustus
  - pisteet TMX:ssä
- **Sim:**
  - seisova mies poterossa on vaikeampi osua kuin avoimella
  - kyykyssä poterossa ei näy
  - tulen alla mies jää poteroon
  - suojaa hakeva mies menee poteroon
  - nousee ampumaan tulen laannuttua
  - hyökkäyksen puolikkaat ovat ryhmät ja pikakivääriryhmä tulittaa ensin
- **Client:**
  - kahden ryhmän siirto: omat muodostelmat 20 m välein
  - yhden ryhmän siirto ennallaan
- **Godot:** käännös ja kuvakaappaus poteroista.

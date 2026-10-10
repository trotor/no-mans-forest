# Haavoittuneen kantaminen (vaihe 8b, skenaarioiden eepos)

Eepos 3 (spec 2026-10-09-roadmap-epics) vaihe 2: lentäjä on yksikkö. Kantajia on yksi tai kaksi, tai talvella
käytetään ahkiota. Kantaminen hidastaa liikettä, ja jos kantaja kaatuu, tavoite palautuu kuten `pick_up`-tavoitteessa.
Käyttäjä 10.10.2026: "tee päätöksiä ja ala toteuttaa". Ahkio tulee talven mukana (8e).

## 1. Kuka kannetaan

Mies, joka ei kykene liikkumaan itse (`Incapacitated`), mutta elää. Kevyesti ja vakavasti haavoittunut kävelee itse.
Kuollutta ei kanneta.

## 2. Käsky ja kulku (`CarryOrder(Unit, Wounded)`)

- **Nostaminen:** kantaja kävelee haavoittuneen luo (1,5 m) ja nostaa hänet selkäänsä 3 sekunnissa. Nostamisen
  aikana kantaja ei ammu.
- **Kantaminen:**
  - Kannettava kulkee kantajan mukana (sama paikka) eikä tee mitään itse.
  - Kantajan nopeus on 55 % omasta. Juokseminen nostaa sen 80 %:iin, ja hiipiminen ja ryömiminen eivät ole
    mahdollisia: kantaja kulkee kyyryssä tai pystyssä.
  - Kantaja ei ammu eikä heitä kranaatteja, koska kädet ovat varattuina. Tulitoiminta ei muutu, vaan kantaja ei vain
    ammu.
- **Toinen kantaja:** `CarryOrder` toiselle miehelle samaan haavoittuneeseen tekee hänestä apukantajan. Silloin
  nopeus on 75 % (juosten 90 %). Apukantaja kulkee kantajan vieressä eikä myöskään ammu.
- **Liikekäskyt** (`MoveOrder`) koskevat kantajaa niin kuin ennenkin. Apukantaja seuraa.
- **Laskeminen maahan:**
  - kantajalle annetaan `DropOrder` (valikon rivi *Laske maahan*)
  - kantajalle annetaan käsky, joka vaatii kädet: ammu, aluetuli, rynnäkkö, tutki tai heitä
  - kantaja kaatuu, lamautuu tai pakenee.

  Kannettava jää siihen kohtaan, jossa kantaja on. Jos apukantaja on mukana, hän ottaa kantamisen hoitaakseen, kun
  kantaja kaatuu.
- **Tavoitteet:**
  - `rescue` lasketaan kannettavan paikasta, joka on sama kuin kantajan.
  - `pick_up`- ja `reach_zone`-tavoitteissa kannettavan mukana olevat esineet eivät siirry kantajalle. Lentäjän
    karttalaukku on lentäjällä.

## 3. Pelaajalle

- Tuplaklikkaus omaan haavoittuneeseen, joka ei kykene liikkumaan, käskee lähimmän komennetun miehen kantamaan. Kuolleen
  kohdalla tuplaklikkaus tutkii kuten ennenkin.
- Oikean napin valikossa on *Kanna*, ja kantajan kohdalla *Laske maahan*.
- Kursorin vihje kertoo *tupla: kanna*.
- Kortit:
  - kantaja: *Kantaa: Saarinen*
  - kannettava: *Kannetaan*
  - apukantaja: *Auttaa kantamaan*
- Kartalla kannettava piirretään kantajan viereen makaavana.

## 4. Testit

- `CarryTests`:
  - kävelee luo ja nostaa
  - kannettava seuraa
  - nopeus 55 % ja 75 % apukantajan kanssa
  - ei ammu
  - laskee maahan käskystä
  - pudottaa kaatuessaan, ja apukantaja jatkaa
  - kävelevää haavoittunutta tai kuollutta ei kanneta (käsky hylätään)
  - kantaminen pysyy samana, kun ajo toistetaan
- `MissionTrackerTests`: kannettu lentäjä kotialueella täyttää rescue-tavoitteen.
- Client:
  - tuplaklikkaus kantaa
  - valikon rivit
  - korttitekstit
  - kursorin teksti

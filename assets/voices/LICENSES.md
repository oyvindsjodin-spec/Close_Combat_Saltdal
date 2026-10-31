# Where the voices come from

The soldiers' voices were spoken by [Piper](https://github.com/rhasspy/piper),
a free text-to-speech program, with the voice models below, and then
processed by `tools/make_voices.py` (made to sound shouted outdoors, some
pitched up or down to make more men). The voice models are downloaded from
Piper's GitHub releases (v0.0.2) when the script runs; they are not kept in
the repository.

| Army | Voice model | Recorded speech it was trained on | Licence of that speech |
| --- | --- | --- | --- |
| British | LibriTTS (high), speakers 42, 135, 342 and 543, with British English pronunciation | [LibriTTS](http://www.openslr.org/60/) (Zen et al., 2019), from LibriVox audiobooks | CC BY 4.0 |
| Norwegian | talesyntese (medium) | [NB Tale / Språkbanken](https://www.nb.no/sprakbanken/en/resource-catalogue/oai-nb-no-sbr-15/), National Library of Norway | CC0 |
| German | Thorsten (low) | [Thorsten-Voice](https://github.com/thorstenMueller/Thorsten-Voice) by Thorsten Müller | CC0 |
| German | Karlsson (low) | [M-AILABS Speech Dataset](https://www.caito.de/2019/01/03/the-m-ailabs-speech-dataset/) | M-AILABS licence (free use, with attribution) |

Attribution for CC BY 4.0: the British voices are derived from LibriTTS by
Heiga Zen, Viet Dang, Rob Clark, Yu Zhang, Ron J. Weiss, Ye Jia, Zhifeng Chen
and Yonghui Wu, licensed under CC BY 4.0
(https://creativecommons.org/licenses/by/4.0/). Changes: synthesised with a
voice model trained on it, then filtered and distorted.

Voice models deliberately not used: Alan (en_GB, all rights reserved), Ryan
and Pavoque (non-commercial licences).

All other sounds in `assets/sounds/` are made from scratch by
`tools/make_sounds.py` and carry no outside licence.

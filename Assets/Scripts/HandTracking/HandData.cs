using System;
using UnityEngine;

/// <summary>
/// Modo de interação — definido pela CONTAGEM DE DEDOS no servidor (cérebro).
/// Independente do ângulo da mão: só importa quantos dedos estão estendidos.
/// </summary>
public enum HandMode
{
    Idle = 0,    // punho solto / palma aberta / sem mão = ocioso (punho SEGURADO = trava, lógica no executor)
    Cursor = 1,  // 1 dedo  -> cursor com raycast (gruda onde o raio bate na janela)
    Move = 2,    // 2 dedos -> agarrar por raycast (apontar p/ janela); u,v = MEIO dos 2 dedos
    Place = 3,   // 3 dedos -> posiciona: mira o celular onde quer a tela
    Scale = 4    // 4 dedos -> escala por slider vertical (subir = maior)
}

/// <summary>
/// Comando pronto que o SERVIDOR (cérebro) manda pro celular executar.
/// O celular NÃO interpreta gesto: ele só executa. Todos os campos batem
/// EXATAMENTE com o JSON de "web app/handtracking/server.py".
/// Trocar/calibrar um gesto = editar server.py e reiniciar (SEM build).
/// </summary>
[Serializable]
public class HandCommand
{
    public int ok;          // 1 se o servidor processou o frame
    public int mode;        // HandMode (0..4), já com debounce aplicado
    public int present;     // 1 se a mão foi detectada neste frame
    public int count;       // nº de dedos estendidos (diagnóstico)
    public float u;         // ponta do indicador, X normalizado 0..1 (espaço de imagem)
    public float v;         // ponta do indicador, Y normalizado 0..1
    public float scaleMul;  // modo 4: multiplicador de escala (1.0 no momento de entrar)
    public float minScale;  // limites de escala vindos do servidor (calibrar sem build)
    public float maxScale;
    public int fx;          // flip eixo X do mapeamento imagem->tela (0/1)
    public int fy;          // flip eixo Y (0/1)
    public float ms;        // latência do processamento no PC (diagnóstico)
    public string state;    // texto de diagnóstico do servidor
    public int hands;       // nº de mãos detectadas (1 = faz os 2 papéis)
    public string handMode; // "L"/"R": mão que dirige os MODOS (esquerda = espaço)
    public string handPtr;  // "L"/"R": mão PONTEIRO (direita = conteúdo remoto)

    public HandMode Mode => (mode >= 0 && mode <= 4) ? (HandMode)mode : HandMode.Idle;
}

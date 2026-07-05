(* F_BlendChildProgress - Blend a child node's progress with its index position
   inside an ordered composite (Sequence, ReactiveSequence).

   Returns overall progress 0..100 as:

       ((nChildIndex - 1) + rChildProgress / 100) / nTotalChildren * 100

   Guards against division by zero when nTotalChildren <= 0.
*)
FUNCTION F_BlendChildProgress : REAL
VAR_INPUT
    nChildIndex     : INT;  // 1-based index of the currently executing child
    nTotalChildren  : INT;  // total number of children in the composite
    rChildProgress  : REAL; // current child's progress, 0..100
END_VAR
    IF nTotalChildren <= 0 THEN
        F_BlendChildProgress := 0.0;
        RETURN;
    END_IF

    F_BlendChildProgress := (INT_TO_REAL(nChildIndex - 1) + rChildProgress / 100.0)
                           / INT_TO_REAL(nTotalChildren) * 100.0;
END_FUNCTION

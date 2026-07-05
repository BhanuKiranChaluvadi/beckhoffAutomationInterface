MDP5001_320_5D7E181{attribute 'TcTypeSystem'}
{attribute 'GUID' := 'F5FCBBDE-5A54-00C3-5F34-3B888CF13936'}
TYPE MDP5001_300_4B3D2459 : 
	STRUCT
		{attribute 'GUID' := 'F5FCBBDF-5A54-00C3-5F34-3B888CF13936'}
		MDP5001_300_Input AT %I* : MDP5001_300_I_4B3D2459;
	END_STRUCT
END_TYPE


{attribute 'TcTypeSystem'}
{attribute 'GUID' := 'F5FCBBDF-5A54-00C3-5F34-3B888CF13936'}
TYPE MDP5001_300_I_4B3D2459 : 
	STRUCT
		{attribute 'GUID' := 'B72D0A2F-BEF6-232C-3679-CAFCA88B7D46'}
		MDP5001_300_Status : Status_2EB64646_Plc;
		MDP5001_300_Value : INT;
	END_STRUCT
END_TYPE


{attribute 'TcTypeSystem'}
{attribute 'GUID' := 'B72D0A2F-BEF6-232C-3679-CAFCA88B7D46'}
TYPE Status_2EB64646_Plc : 
	STRUCT
		Underrange : BIT;
		Overrange : BIT;
		{attribute 'GUID' := '18071995-0000-0000-0000-000000000010}
		Limit_1_Bit0 : BIT;
		{attribute 'GUID' := '18071995-0000-0000-0000-000000000010}
		Limit_1_Bit1 : BIT;
		{attribute 'GUID' := '18071995-0000-0000-0000-000000000010}
		Limit_2_Bit0 : BIT;
		{attribute 'GUID' := '18071995-0000-0000-0000-000000000010}
		Limit_2_Bit1 : BIT;
		Error : BIT;
		{attribute 'hide'}
		_reserved1 : BIT;
		{attribute 'hide'}
		_reserved2 : BIT;
		{attribute 'hide'}
		_reserved3 : BIT;
		{attribute 'hide'}
		_reserved4 : BIT;
		{attribute 'hide'}
		_reserved5 : BIT;
		{attribute 'hide'}
		_reserved6 : BIT;
		{attribute 'hide'}
		_reserved7 : BIT;
		TxPDO_State : BIT;
		TxPDO_Toggle : BIT;
	END_STRUCT
END_TYPE

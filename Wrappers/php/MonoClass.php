<?php

class MonoClass
{
    private static $sSocket = null;
    public static $DaemonSocket = '/tmp/monodaemon';

    private static $END = "\x00";
    private static $STATE_STATIC_SET_PROPERTY = "\x01";
    private static $STATE_STATIC_GET_PROPERTY = "\x02";
    private static $STATE_STATIC_CALL = "\x03";
    private static $STATE_NEW_CLASS = "\x04";
    private static $STATE_DESTROY_CLASS = "\x05";
    private static $STATE_GET_CLASS_PROPERTY = "\x06";
    private static $STATE_SET_CLASS_PROPERTY = "\x07";
    private static $STATE_CALL_CLASS_METHOD = "\x08";

    private static $STATE_TYPE = "\x20";
    private static $STATE_ARGUMENT = "\x40";


    private static $TYPE_NULL = "\x01";
    private static $TYPE_POINTER = "\x02";
    private static $TYPE_EXCEPTION = "\x03";
    private static $TYPE_STRING = "\x04";
    private static $TYPE_INT = "\x05";
    private static $TYPE_FLOAT = "\x06";
    private static $TYPE_BOOL = "\x07";

    private $mName;
    public $_MonoClassHash;

    public static function destroy() {
        if (MonoClass::$sSocket === null) return;
        socket_write(MonoClass::$sSocket, MonoClass::$END);
    }

    private static function send($pData) {
        if (socket_write(MonoClass::$sSocket, $pData) === false) {
            throw new Exception('Error writing to socket.');
        }
    }

    private static function recv($pBuffer = null) {
        $tResult = $pBuffer === null ? '' : $pBuffer;
        $tBuffer = '';
        if (socket_recv(MonoClass::$sSocket, $tBuffer, 256, 0) === false) {
            throw new Exception('Error reading socket.');
        }
        if ($tBuffer === '') {
            return false;
        }
        $tResult .= $tBuffer;
        return $tResult;
    }

    function __construct($pName, $pHash = null) {
        if (MonoClass::$sSocket === null) {
            MonoClass::initializeSocket();
        }
        $this->mName = $pName;

        if ($pHash === null) {
            $pHash = '';
            MonoClass::send(MonoClass::$STATE_NEW_CLASS . $pName . MonoClass::$END);
            while (true) {
                $pHash .= MonoClass::recv($pHash);
                if (strlen($pHash) === 4) {
                    break;
                }
            }
        }
        $this->_MonoClassHash = $pHash;
    }

    private function _createArgs($pArgs) {
        if (count($pArgs) === 0) return '';

        $tArgs = '';
        for ($i = 0, $il = count($pArgs); $i < $il; $i++) {
            $tArg = $pArgs[$i];
            if (is_null($tArg)) {
                $tArgs .= MonoClass::$TYPE_NULL;
            } else if (is_string($tArg)) {
                $tArgs .= MonoClass::$TYPE_STRING . $tArg . MonoClass::$END;
            } else if (is_bool($tArg)) {
                $tArgs .= MonoClass::$TYPE_BOOL . ($tArg === true ? "\x01" : "\x00");
            } else if (is_int($tArg)) {
                $tArgs .= MonoClass::$TYPE_INT . 
                    pack('N', $tArg >= pow(2, 15) ? ($tArg - pow(2, 16)) : $tArg);
            } else if (is_float($tArg)) {
                throw new Exception('Floats are not yet supported as arguments.');
            } else if (is_object($tArg)) {
                if (is_a($tArg, 'MonoClass')) {
                    $tArgs .= MonoClass::$TYPE_POINTER . $tArg->_MonoClassHash;
                } else {
                    throw new Exception('Generic objects are not yet supported for arguments.');
                }
            } else if (is_array($tArg)) {
                throw new Exception('Arrays are not yet supported for arguments.');
            }
        }

        return MonoClass::$STATE_ARGUMENT . $tArgs . MonoClass::$END;
    }

    private static function _getObject() {
        $tData = MonoClass::recv();
        $tType = $tData[0];
        $tData = substr($tData, 1);

        while (true) {
            switch ($tType) {
                case MonoClass::$TYPE_NULL:
                    return null;
                case MonoClass::$TYPE_POINTER:
                    return new MonoClass(null, $tData);
                case MonoClass::$TYPE_STRING:
                    if ($tData[strlen($tData) - 1] === "\x00") {
                        return substr($tData, 0, strlen($tData) - 1);
                    } else {
                        $tData .= MonoClass::recv($tData);
                    }
                    break;
                case MonoClass::$TYPE_INT:
                    if (strlen($tData) === 4) {
                        $tData = unpack('N', $tData);
                        $tData = $tData[1];
                        if ($tData >= pow(2, 31)) $tData -= pow(2, 32);
                        return $tData;
                    } else {
                        $tData .= MonoClass::recv($tData);
                    }
                    break;
                case MonoClass::$TYPE_FLOAT:
                    throw new Exception('Floats are not yet supported as return types.');
                case MonoClass::$TYPE_BOOL:
                    if (strlen($tData) === 1) {
                        return $tData === "\x00" ? false : true;
                    } else {
                        $tData .= MonoClass::recv($tData);
                    }
                    break;
                default:
                    throw new Exception('Unsupported object type returned.');
            }
        }
    }

    public function __call($pName, $pArgs) {
        MonoClass::send(MonoClass::$STATE_CALL_CLASS_METHOD . $this->_MonoClassHash . $pName . MonoClass::$END . $this->_createArgs($pArgs) . MonoClass::$END);

        $result = MonoClass::_getObject();
        return $result;
    }

    public function __get($pName) {
    }

    public function __set($pName, $pValue) {
    }

    private static function initializeSocket() {
        MonoClass::$sSocket = socket_create(AF_UNIX, SOCK_STREAM, 0);
        if (MonoClass::$sSocket === false) {
            throw new Exception('Could not create socket.');
        }

        if (socket_connect(MonoClass::$sSocket, '/tmp/monodaemon') === false) {
            throw new Exception('Could not connect to socket.');
        }
    }


}

$tTest = new MonoClass('MonoDaemon.MainClass');
echo $tTest->MyTestMethod() . "\n";
echo $tTest->MyTestMethodString("Test string") . "\n";
echo $tTest->MyTestMethodInt(12345) . "\n";
echo $tTest->MyTestMethodInt(12) . "\n";
echo $tTest->MyTestMethodInt(0xFFFFFFFF) . "\n";
echo $tTest->MyTestMethodInt(0x7FFFFFFF) . "\n";
echo $tTest->MyTestMethodInt(0x80000000) . "\n";
echo $tTest->MyTestMethodInt(0) . "\n";
echo $tTest->MyTestMethodInt(-12) . "\n";
echo $tTest->MyTestMethodInt(-122343) . "\n";
//echo $tTest->MyTestMethodFloat(1.34) . "\n";
echo $tTest->MyTestMethodBool(true) . "\n";
MonoClass::destroy();


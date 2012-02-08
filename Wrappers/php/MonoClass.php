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
    private static $TYPE_VOID = "\x08";

    private static $objects = null;

    private $mName;
    public $_MonoClassHash;

    public static function destroy() {
        if (MonoClass::$sSocket === null) return;
        socket_write(MonoClass::$sSocket, MonoClass::$END);
        try {
            socket_close(MonoClass::$sSocket);
        } catch (Exception $e) {}
        MonoClass::$sSocket = null;
        MonoClass::$objects = null;
    }

    private static function send($pData) {
        if (socket_write(MonoClass::$sSocket, $pData) === false) {
            throw new Exception('Error writing to socket.');
        }
    }

    private static function recv() {
        $tBuffer = '';
        if (socket_recv(MonoClass::$sSocket, $tBuffer, 1, 0) === false) {
            throw new Exception('Error reading socket.');
        }
        if ($tBuffer === '') {
            return false;
        }
        return $tBuffer;
    }

    private static function destroyObject(&$pObject) {
        unset(MonoClass::$objects[$pObject->_MonoClassHash]);
    }

    private static function generateFromPointer($pPointer) {
        $tObject = new MonoClass();
        $tObject->_MonoClassHash = $pPointer;
        MonoClass::$objects[$pPointer] = $tObject;
        return $tObject;
    } 

    function __construct($pName = null) {
        if (MonoClass::$sSocket === null) {
            MonoClass::initializeSocket();
        }

        if ($pName === null) {
            return;
        }

        $this->mName = $pName;
        $pHash = '';
        if (func_num_args() === 1) {
            MonoClass::send(MonoClass::$STATE_NEW_CLASS . $pName . MonoClass::$END . MonoClass::$END);
        } else {
            MonoClass::send(MonoClass::$STATE_NEW_CLASS . $pName . MonoClass::$END . MonoClass::_createArgs(array_slice(func_get_args(), 1)) . MonoClass::$END);
        }
        while (true) {
            $pHash .= MonoClass::recv();
            if (strlen($pHash) === 4) {
                break;
            }
        }
        $this->_MonoClassHash = $pHash;

        MonoClass::$objects[$this->_MonoClassHash] = $this;
    }

    function __destruct() {
        if (MonoClass::$sSocket !== null) {
            MonoClass::send(MonoClass::$STATE_DESTROY_CLASS . $this->_MonoClassHash);
        }
    }

    private static function _createObject($pObject) {
        if (is_null($pObject)) {
            return MonoClass::$TYPE_NULL;
        } else if (is_string($pObject)) {
            return MonoClass::$TYPE_STRING . $pObject . MonoClass::$END;
        } else if (is_bool($pObject)) {
            return MonoClass::$TYPE_BOOL . ($pObject === true ? "\x01" : "\x00");
        } else if (is_int($pObject)) {
            return MonoClass::$TYPE_INT . 
                pack('N', $pObject >= pow(2, 15) ? ($pObject - pow(2, 16)) : $pObject);
        } else if (is_float($pObject)) {
            throw new Exception('Floats are not yet supported as arguments.');
        } else if (empty($pObject)) {
            return MonoClass::$TYPE_VOID;
        } else if (is_object($pObject)) {
            if (is_a($pObject, 'MonoClass')) {
                return MonoClass::$TYPE_POINTER . $pObject->_MonoClassHash;
            } else {
                throw new Exception('Generic objects are not yet supported for arguments.');
            }
        } else if (is_array($pObject)) {
            throw new Exception('Arrays are not yet supported for arguments.');
        }
    }

    private static function _createArgs($pArgs) {
        if (count($pArgs) === 0) return '';

        $tArgs = '';
        for ($i = 0, $il = count($pArgs); $i < $il; $i++) {
            $tArgs .= MonoClass::_createObject($pArgs[$i]);
        }

        return MonoClass::$STATE_ARGUMENT . $tArgs . MonoClass::$END;
    }

    private static function _getObject() {
        $tType = MonoClass::recv();
        $tData = '';

        while (true) {
            switch ($tType) {
                case MonoClass::$TYPE_NULL:
                    return null;
                case MonoClass::$TYPE_POINTER:
                    $tData .= MonoClass::recv();
                    if (strlen($tData) === 4) {
                        if (array_key_exists($tData, MonoClass::$objects)) {
                            return MonoClass::$objects[$tData];
                        } else {
                            return MonoClass::generateFromPointer($tData);
                        }
                    }
                    break;
                case MonoClass::$TYPE_STRING:
                    $tData .= MonoClass::recv();
                    if ($tData[strlen($tData) - 1] === "\x00") {
                        return substr($tData, 0, strlen($tData) - 1);
                    }
                    break;
                case MonoClass::$TYPE_INT:
                    $tData .= MonoClass::recv();
                    if (strlen($tData) === 4) {
                        $tData = unpack('N', $tData);
                        $tData = $tData[1];
                        if ($tData >= pow(2, 31)) $tData -= pow(2, 32);
                        return $tData;
                    }
                    break;
                case MonoClass::$TYPE_FLOAT:
                    throw new Exception('Floats are not yet supported as return types.');
                case MonoClass::$TYPE_BOOL:
                    $tData .= MonoClass::recv();
                    if (strlen($tData) === 1) {
                        return $tData === "\x00" ? false : true;
                    }
                    break;
                case MonoClass::$TYPE_VOID:
                    return;
                default:
                    throw new Exception('Unsupported object type returned.');
            }
        }
    }

    public function __call($pName, $pArgs) {
        MonoClass::send(MonoClass::$STATE_CALL_CLASS_METHOD . $this->_MonoClassHash . $pName . MonoClass::$END . MonoClass::_createArgs($pArgs) . MonoClass::$END);

        $result = MonoClass::_getObject();
        return $result;
    }

    public static function callStatic($pClassName, $pName, $pArgs) {
        MonoClass::send(MonoClass::$STATE_STATIC_CALL . $pClassName . MonoClass::$END . $pName . MonoClass::$END . MonoClass::_createArgs($pArgs) . MonoClass::$END);

        $result = MonoClass::_getObject();
        return $result;
    }

    public static function getStatic($pClassName, $pName) {

    }

    public static function setStatic($pClassName, $pName, $pValue) {

    }

    public function __get($pName) {
        MonoClass::send(MonoClass::$STATE_GET_CLASS_PROPERTY . $this->_MonoClassHash . $pName . MonoClass::$END);

        $result = MonoClass::_getObject();
        return $result;
    }

    public function __set($pName, $pValue) {
        MonoClass::send(MonoClass::$STATE_SET_CLASS_PROPERTY . $this->_MonoClassHash . $pName . MonoClass::$END . MonoClass::_createObject($pValue));
    }

    public function __toString() {
        return $this->ToString();
    }

    private static function initializeSocket() {
        MonoClass::$sSocket = socket_create(AF_UNIX, SOCK_STREAM, 0);
        if (MonoClass::$sSocket === false) {
            throw new Exception('Could not create socket.');
        }

        if (socket_connect(MonoClass::$sSocket, '/tmp/monodaemon') === false) {
            throw new Exception('Could not connect to socket.');
        }
        MonoClass::$objects = array();
    }


}

